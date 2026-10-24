using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Plumy.Api.ClientCompanies;
using Plumy.Api.Firms;
using Plumy.Api.Infrastructure;
using Plumy.Api.Invoices;
using Plumy.Application;
using Plumy.Infrastructure;
using Plumy.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructure();
// Sends uploaded invoices to the worker through Service Bus (see ADR 0003)
builder.AddInvoiceExtractionQueue();
builder.Services.AddApplication();

builder.Services.AddProblemDetails();
// .NET 10: validates DataAnnotations on endpoint parameters and returns a 400 ValidationProblem
builder.Services.AddValidation();
builder.Services.AddExceptionHandler<BusinessExceptionHandler>();

// Entra External ID access tokens, required on every endpoint by default (see EntraAuthentication)
builder.Services.AddEntraAuthentication(builder.Configuration, builder.Environment);
// Multi-tenancy: the user's firm (looked up by their oid) scopes every tenant endpoint
builder.Services.AddFirmMembership();

// In Azure, TLS terminates at the Container Apps ingress, which forwards plain HTTP plus X-Forwarded-* headers.
// Without this the app would think every request is HTTP and log the ingress IP instead of the client's.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // The ingress IPs are not known in advance; the API is only reachable through it (internal ingress)
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// First, so everything after it sees the original scheme and client IP
app.UseForwardedHeaders();

app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();

    // Dev only: in Azure, migrations will run from the pipeline as a bundle (v0.2)
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<PlumyDbContext>().Database.MigrateAsync();
    // Same idea for Azurite: in Azure the blob container is declared in Bicep
    await app.Services.CreateStorageContainersAsync();

    // In Azure the ingress already redirects HTTP to HTTPS, and the container has no HTTPS port to redirect to
    app.UseHttpsRedirection();
}

app.MapFirmEndpoints();
app.MapInvitationEndpoints();
app.MapMemberEndpoints();
app.MapClientCompanyEndpoints();
app.MapInvoiceEndpoints();

await app.RunAsync();
