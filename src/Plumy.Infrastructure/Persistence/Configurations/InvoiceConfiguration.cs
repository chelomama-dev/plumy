using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Firms;
using Plumy.Domain.Invoices;

namespace Plumy.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceConfiguration(PlumyDbContext db) : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.FileName)
            .HasMaxLength(Invoice.MaxFileNameLength)
            .IsRequired();

        // As text: readable in SQL and safe if the enum is ever reordered
        builder.Property(i => i.Format)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsUnicode(false);

        builder.Property(i => i.Sha256)
            .HasMaxLength(Invoice.Sha256Length)
            .IsUnicode(false)
            .IsFixedLength();

        builder.Property(i => i.StoragePath)
            .HasMaxLength(Invoice.MaxStoragePathLength)
            .IsUnicode(false);

        // Restrict, as with companies: deleting a firm or a company must never take its invoices with it
        builder.HasOne<Firm>()
            .WithMany()
            .HasForeignKey(i => i.FirmId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ClientCompany>()
            .WithMany()
            .HasForeignKey(i => i.ClientCompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(QueryFilters.CurrentFirm, i => i.FirmId == db.CurrentFirmId);

        // The main list: the firm's invoices, newest first
        builder.HasIndex(i => new { i.FirmId, i.UploadedAt });

        // The same file can't be uploaded twice for the same company. Also serves the filter by company
        builder.HasIndex(i => new { i.ClientCompanyId, i.Sha256 }).IsUnique();
    }
}
