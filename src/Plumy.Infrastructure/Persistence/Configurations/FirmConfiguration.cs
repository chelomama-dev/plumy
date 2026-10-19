using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plumy.Domain.Firms;

namespace Plumy.Infrastructure.Persistence.Configurations;

internal sealed class FirmConfiguration : IEntityTypeConfiguration<Firm>
{
    public void Configure(EntityTypeBuilder<Firm> builder)
    {
        builder.ToTable("Firms");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();

        builder.Property(f => f.Name)
            .HasMaxLength(Firm.MaxNameLength)
            .IsRequired();

        // The firm owns its users: they are saved with it and go away with it
        builder.HasMany(f => f.Users)
            .WithOne()
            .HasForeignKey(u => u.FirmId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(f => f.Users).HasField("_users");
    }
}

internal sealed class FirmUserConfiguration : IEntityTypeConfiguration<FirmUser>
{
    // Longest FirmRole name, with room to grow
    private const int RoleMaxLength = 20;

    public void Configure(EntityTypeBuilder<FirmUser> builder)
    {
        builder.ToTable("FirmUsers");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        // One firm per user for now. The index also makes the per-request membership lookup a single seek
        builder.HasIndex(u => u.ObjectId).IsUnique();

        // Stored as text so the table reads well and reordering the enum can't change anyone's role
        builder.Property(u => u.Role)
            .HasConversion<string>()
            .HasMaxLength(RoleMaxLength)
            .IsUnicode(false)
            .IsRequired();
    }
}
