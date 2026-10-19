using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Plumy.Application.ClientCompanies.ArchiveClientCompany;
using Plumy.Application.ClientCompanies.CreateClientCompany;
using Plumy.Application.ClientCompanies.GetClientCompanyById;
using Plumy.Application.ClientCompanies.ListClientCompanies;
using Plumy.Application.ClientCompanies.RenameClientCompany;
using Plumy.Application.Firms.CreateFirm;
using Plumy.Application.Firms.GetCurrentUser;

namespace Plumy.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<CreateClientCompanyHandler>();
        services.AddScoped<GetClientCompanyByIdHandler>();
        services.AddScoped<ListClientCompaniesHandler>();
        services.AddScoped<RenameClientCompanyHandler>();
        services.AddScoped<ArchiveClientCompanyHandler>();

        services.AddScoped<CreateFirmHandler>();
        services.AddScoped<GetCurrentUserHandler>();

        // Real clock in production; tests pass a fixed one. TryAdd lets the host register its own first
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
