using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plumy.Domain.Accounting;
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

        builder.Property(i => i.ExtractionStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsUnicode(false);

        builder.Property(i => i.ExtractionError)
            .HasMaxLength(Invoice.MaxExtractionErrorLength);

        // Columns, not JSON: later steps search by supplier NIF and invoice number (duplicates, review).
        // All of them nullable, so a shadow discriminator column tells "nothing read yet" (null) apart from "read,
        // but every field came back empty"
        builder.ComplexProperty(i => i.ExtractedData, data =>
        {
            data.HasDiscriminator();
            data.Property<string>("Discriminator").HasMaxLength(50).IsUnicode(false);
            data.Property(d => d.SupplierName).HasMaxLength(ExtractedInvoiceData.MaxSupplierNameLength);
            data.Property(d => d.SupplierTaxId)
                .HasMaxLength(ExtractedInvoiceData.MaxSupplierTaxIdLength)
                .IsUnicode(false);
            data.Property(d => d.InvoiceNumber).HasMaxLength(ExtractedInvoiceData.MaxInvoiceNumberLength);
            data.Property(d => d.NetAmount).HasPrecision(18, 2);
            data.Property(d => d.TaxAmount).HasPrecision(18, 2);
            data.Property(d => d.TotalAmount).HasPrecision(18, 2);
            data.Property(d => d.Currency)
                .HasMaxLength(ExtractedInvoiceData.CurrencyLength)
                .IsUnicode(false)
                .IsFixedLength();
            data.Property(d => d.ItemsDescription).HasMaxLength(ExtractedInvoiceData.MaxItemsDescriptionLength);
        });

        // Code and source are required inside it, so EF tells "no suggestion" (all null) apart without a discriminator
        builder.ComplexProperty(i => i.LedgerAccountSuggestion, suggestion =>
        {
            suggestion.Property(s => s.Code).HasMaxLength(LedgerAccount.MaxCodeLength).IsUnicode(false);
            suggestion.Property(s => s.Source)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsUnicode(false);
            suggestion.Property(s => s.Reason).HasMaxLength(LedgerAccountSuggestion.MaxReasonLength);
        });

        builder.Property(i => i.LedgerAccountCode)
            .HasMaxLength(LedgerAccount.MaxCodeLength)
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

        // The suggestions' history: a company's confirmed accounts, newest first. Filtered, so the index only holds
        // the invoices that teach anything
        builder.HasIndex(i => new { i.ClientCompanyId, i.LedgerAccountConfirmedAt })
            .HasFilter("[LedgerAccountCode] IS NOT NULL");
    }
}
