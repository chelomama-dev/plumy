using Microsoft.EntityFrameworkCore;
using Plumy.Api.ClientCompanies;
using Plumy.Api.Infrastructure;
using Plumy.Application;
using Plumy.Infrastructure;
using Plumy.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructure();
builder.Services.AddApplication();

builder.Services.AddProblemDetails();
// .NET 10: validates DataAnnotations on endpoint parameters and returns a 400 ValidationProblem
builder.Services.AddValidation();
builder.Services.AddExceptionHandler<BusinessExceptionHandler>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Dev only: in Azure, migrations will run from the pipeline as a bundle (v0.2)
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<PlumyDbContext>().Database.MigrateAsync();
}

app.UseHttpsRedirection();

app.MapClientCompanyEndpoints();

await app.RunAsync();
