using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Common;

namespace Plumy.Infrastructure.Persistence.Configurations;

internal sealed class ClientCompanyConfiguration : IEntityTypeConfiguration<ClientCompany>
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

        // Soft delete. The unique index below still covers archived rows on purpose: they keep their tax ID
        builder.HasQueryFilter(QueryFilters.NotArchived, c => c.ArchivedAt == null);

        // TODO v0.3: unique per accounting firm (FirmId, TaxId) once multi-tenancy lands
        builder.HasIndex(c => c.TaxId).IsUnique();
    }
}
