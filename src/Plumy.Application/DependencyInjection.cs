using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Plumy.Application.ClientCompanies.ArchiveClientCompany;
using Plumy.Application.ClientCompanies.CreateClientCompany;
using Plumy.Application.ClientCompanies.GetClientCompanyById;
using Plumy.Application.ClientCompanies.ListClientCompanies;
using Plumy.Application.ClientCompanies.RenameClientCompany;
using Plumy.Application.Firms.AcceptInvitation;
using Plumy.Application.Firms.CreateFirm;
using Plumy.Application.Firms.GetCurrentUser;
using Plumy.Application.Firms.InviteMember;
using Plumy.Application.Firms.ListInvitations;
using Plumy.Application.Firms.ListMembers;
using Plumy.Application.Firms.RemoveMember;
using Plumy.Application.Firms.RevokeInvitation;
using Plumy.Application.Invoices.ExtractInvoice;
using Plumy.Application.Invoices.GetInvoiceById;
using Plumy.Application.Invoices.GetInvoiceFile;
using Plumy.Application.Invoices.ListInvoices;
using Plumy.Application.Invoices.RetryExtraction;
using Plumy.Application.Invoices.UploadInvoice;

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
        services.AddScoped<InviteMemberHandler>();
        services.AddScoped<ListInvitationsHandler>();
        services.AddScoped<RevokeInvitationHandler>();
        services.AddScoped<AcceptInvitationHandler>();
        services.AddScoped<ListMembersHandler>();
        services.AddScoped<RemoveMemberHandler>();

        services.AddScoped<UploadInvoiceHandler>();
        services.AddScoped<ListInvoicesHandler>();
        services.AddScoped<GetInvoiceByIdHandler>();
        services.AddScoped<GetInvoiceFileHandler>();
        services.AddScoped<RetryInvoiceExtractionHandler>();
        services.AddScoped<ExtractInvoiceHandler>();

        // Real clock in production; tests pass a fixed one. TryAdd lets the host register its own first
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
