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

// Stripe (ADR 0012), only if its keys are in the AppHost's user-secrets (Stripe:SecretKey, Stripe:WebhookSecret).
// Without them the API runs with payments off, so nobody needs a Stripe account to work on the rest
if (!string.IsNullOrWhiteSpace(builder.Configuration["Stripe:SecretKey"]))
{
    var stripeKey = builder.AddParameterFromConfiguration("stripe-secret-key", "Stripe:SecretKey", secret: true);
    var stripeWebhookSecret =
        builder.AddParameterFromConfiguration("stripe-webhook-secret", "Stripe:WebhookSecret", secret: true);

    api.WithEnvironment("Stripe__SecretKey", stripeKey)
        .WithEnvironment("Stripe__WebhookSecret", stripeWebhookSecret)
        // Where Stripe sends people back: the billing page of the Angular app below
        .WithEnvironment("Stripe__ReturnUrl", "http://localhost:4200/billing");

    // Stripe can't reach localhost, so the Stripe CLI listens for the account's events and forwards them to the
    // API. Needs `stripe login` once; the secret it signs with is the one `stripe listen --print-secret` shows
    builder.AddExecutable("stripe-webhooks", "stripe", ".",
            "listen",
            "--events", "checkout.session.completed,customer.subscription.created,customer.subscription.updated," +
                        "customer.subscription.deleted,customer.subscription.paused,customer.subscription.resumed",
            "--forward-to", ReferenceExpression.Create($"{api.GetEndpoint("http")}/api/billing/stripe-webhook"))
        .WithParentRelationship(api)
        .WaitFor(api);
}

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
