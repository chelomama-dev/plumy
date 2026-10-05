var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql", port: 14330)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume();

var db = sql.AddDatabase("plumydb");

builder.AddProject<Projects.Plumy_Api>("api")
    .WithReference(db)
    .WaitFor(db);

builder.Build().Run();
