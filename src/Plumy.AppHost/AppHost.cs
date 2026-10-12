var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql", port: 14330)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume();

var db = sql.AddDatabase("plumydb");

var api = builder.AddProject<Projects.Plumy_Api>("api")
    .WithReference(db)
    .WaitFor(db);

// Angular dev server. The browser only talks to it; it proxies /api to the API (web/proxy.conf.js),
// so there is no CORS. WithReference injects the API URL as services__api__{scheme}__0.
builder.AddJavaScriptApp("web", "../../web", runScriptName: "start")
    .WithPnpm()
    .WithHttpEndpoint(env: "PORT")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
