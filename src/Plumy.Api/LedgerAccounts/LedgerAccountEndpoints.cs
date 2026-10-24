using Microsoft.AspNetCore.Http.HttpResults;
using Plumy.Api.Infrastructure;
using Plumy.Application.Invoices;
using Plumy.Domain.Accounting;

namespace Plumy.Api.LedgerAccounts;

internal static class LedgerAccountEndpoints
{
    public static IEndpointRouteBuilder MapLedgerAccountEndpoints(this IEndpointRouteBuilder app)
    {
        // The accounts an invoice can go to, for the picker. The same for every firm (the PGC), so no paging
        app.MapGet("/api/ledger-accounts", List)
            .WithTags("Ledger accounts")
            .WithName("ListLedgerAccounts")
            .RequireFirmMembership();

        return app;
    }

    private static Ok<IEnumerable<LedgerAccountResponse>> List() =>
        TypedResults.Ok(ChartOfAccounts.All.Select(LedgerAccountResponse.From));
}
