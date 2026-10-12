using Microsoft.Extensions.DependencyInjection;
using Plumy.Application.ClientCompanies.CreateClientCompany;
using Plumy.Application.ClientCompanies.GetClientCompanyById;

namespace Plumy.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<CreateClientCompanyHandler>();
        services.AddScoped<GetClientCompanyByIdHandler>();

        return services;
    }
}
