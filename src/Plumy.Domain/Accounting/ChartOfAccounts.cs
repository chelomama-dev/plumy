namespace Plumy.Domain.Accounting;

/// <summary>
/// The accounts a supplier invoice can go to: purchases and services (group 6) and fixed assets (group 2) of the
/// PGC, at the 3-digit level every firm shares. Each firm's own subaccounts (6280001...) are an export concern.
/// In code, not in the database: it is the law's list, it changes with a new PGC, not with a customer.
/// </summary>
public static class ChartOfAccounts
{
    public static IReadOnlyList<LedgerAccount> All { get; } =
    [
        new("600", "Compras de mercaderías"),
        new("601", "Compras de materias primas"),
        new("602", "Compras de otros aprovisionamientos"),
        new("607", "Trabajos realizados por otras empresas"),
        new("621", "Arrendamientos y cánones"),
        new("622", "Reparaciones y conservación"),
        new("623", "Servicios de profesionales independientes"),
        new("624", "Transportes"),
        new("625", "Primas de seguros"),
        new("626", "Servicios bancarios y similares"),
        new("627", "Publicidad, propaganda y relaciones públicas"),
        new("628", "Suministros"),
        new("629", "Otros servicios"),
        new("631", "Otros tributos"),
        new("206", "Aplicaciones informáticas"),
        new("213", "Maquinaria"),
        new("214", "Utillaje"),
        new("215", "Otras instalaciones"),
        new("216", "Mobiliario"),
        new("217", "Equipos para procesos de información"),
        new("218", "Elementos de transporte"),
        new("219", "Otro inmovilizado material")
    ];

    private static readonly Dictionary<string, LedgerAccount> ByCode = All.ToDictionary(a => a.Code);

    public static LedgerAccount? Find(string? code) => code is null ? null : ByCode.GetValueOrDefault(code);
}
