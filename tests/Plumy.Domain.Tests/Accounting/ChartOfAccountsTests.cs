using Plumy.Domain.Accounting;

namespace Plumy.Domain.Tests.Accounting;

public class ChartOfAccountsTests
{
    [Fact]
    public void Codes_are_unique_and_fit_the_column()
    {
        Assert.Equal(ChartOfAccounts.All.Count, ChartOfAccounts.All.Select(a => a.Code).Distinct().Count());
        Assert.All(ChartOfAccounts.All, a => Assert.InRange(a.Code.Length, 3, LedgerAccount.MaxCodeLength));
    }

    [Fact]
    public void Only_expenses_and_fixed_assets()
    {
        // A supplier invoice is a purchase, a service or an asset: never income (7) or a balance account (4)
        Assert.All(ChartOfAccounts.All, a => Assert.Contains(a.Code[0], "62"));
    }

    [Theory]
    [InlineData("628", "Suministros")]
    [InlineData("623", "Servicios de profesionales independientes")]
    public void Finds_an_account_by_code(string code, string name)
    {
        Assert.Equal(new LedgerAccount(code, name), ChartOfAccounts.Find(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("6280001")]
    [InlineData("700")]
    public void Anything_else_is_not_found(string? code)
    {
        Assert.Null(ChartOfAccounts.Find(code));
    }
}
