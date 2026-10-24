var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql", port: 14330)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume();

var db = sql.AddDatabase("plumydb");

// Azurite, the Blob Storage emulator, where uploaded invoices live. The API creates its container on startup
var blobs = builder.AddAzureStorage("storage")
    .RunAsEmulator(azurite => azurite
        .WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume())
    .AddBlobs("blobs");

// Service Bus emulator (a container plus the SQL Server it keeps its state in). The API sends each uploaded
// invoice to the queue; the worker reads it. Queue settings match Bicep
var messaging = builder.AddAzureServiceBus("messaging")
    .RunAsEmulator();

messaging.AddServiceBusQueue("invoice-extraction")
    .WithProperties(queue =>
    {
        // A failed delivery is retried when its lock expires, so this also spaces the retries out
        queue.LockDuration = TimeSpan.FromMinutes(1);
        // Same value as the worker's InvoiceExtraction:MaxDeliveryCount
        queue.MaxDeliveryCount = 5;
    });

var api = builder.AddProject<Projects.Plumy_Api>("api")
    .WithReference(db)
    .WaitFor(db)
    .WithReference(blobs)
    .WaitFor(blobs)
    .WithReference(messaging)
    .WaitFor(messaging);

// Reads the invoices in the background. Waits for the API, which applies the migrations in Development
builder.AddProject<Projects.Plumy_Worker>("worker")
    .WithReference(db)
    .WithReference(blobs)
    .WithReference(messaging)
    .WaitFor(api);

// Angular dev server. The browser only talks to it; it proxies /api to the API (web/proxy.conf.js),
// so there is no CORS. WithReference injects the API URL as services__api__{scheme}__0.
builder.AddJavaScriptApp("web", "../../web", runScriptName: "start")
    .WithPnpm()
    // Fixed port: Entra only redirects back to the URIs registered in plumy-web (http://localhost:4200)
    .WithHttpEndpoint(port: 4200, env: "PORT")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
