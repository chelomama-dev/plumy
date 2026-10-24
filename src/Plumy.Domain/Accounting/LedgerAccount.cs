namespace Plumy.Domain.Accounting;

/// <summary>An account of the Spanish chart of accounts (Plan General Contable), e.g. 628 Suministros.</summary>
public sealed record LedgerAccount(string Code, string Name)
{
    /// <summary>Room for the subaccounts firms use (6280001), although the catalog only has PGC accounts.</summary>
    public const int MaxCodeLength = 10;
}
