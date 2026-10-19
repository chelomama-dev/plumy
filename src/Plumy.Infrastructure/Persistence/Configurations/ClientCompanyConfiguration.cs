using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;
using Plumy.Domain.Firms;

namespace Plumy.Infrastructure.Persistence.Configurations;

internal sealed class ClientCompanyConfiguration(PlumyDbContext db) : IEntityTypeConfiguration<ClientCompany>
{
    // NIF, NIE and CIF are always 9 ASCII characters once normalized
    private const int TaxIdLength = 9;

    public void Configure(EntityTypeBuilder<ClientCompany> builder)
    {
        builder.ToTable("ClientCompanies");

        // The domain generates the Id (GUID v7), never the database
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Name)
            .HasMaxLength(ClientCompany.MaxNameLength)
            .IsRequired();

        builder.Property(c => c.TaxId)
            .HasConversion(taxId => taxId.Value, value => TaxId.Create(value))
            .HasMaxLength(TaxIdLength)
            .IsUnicode(false)
            .IsFixedLength()
            .IsRequired();

        // Restrict: a firm with client companies can't be deleted by accident and take their books with it
        builder.HasOne<Firm>()
            .WithMany()
            .HasForeignKey(c => c.FirmId)
            .OnDelete(DeleteBehavior.Restrict);

        // Multi-tenancy: every query only sees the current firm's companies, whatever the handler asks for
        builder.HasQueryFilter(QueryFilters.CurrentFirm, c => c.FirmId == db.CurrentFirmId);

        // Soft delete. The unique index below still covers archived rows on purpose: they keep their tax ID
        builder.HasQueryFilter(QueryFilters.NotArchived, c => c.ArchivedAt == null);

        // Unique per firm: two firms can keep the books of the same company. FirmId first also serves the
        // tenant filter on every list and search
        builder.HasIndex(c => new { c.FirmId, c.TaxId }).IsUnique();
    }
}
