using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plumy.Domain.Billing;
using Plumy.Domain.Common;
using Plumy.Domain.Firms;
using Plumy.Domain.Invoices;

namespace Plumy.Infrastructure.Persistence.Configurations;

internal sealed class FirmConfiguration : IEntityTypeConfiguration<Firm>
{
    // Longest Plan and SubscriptionStatus names, with room to grow
    private const int EnumMaxLength = 20;

    public void Configure(EntityTypeBuilder<Firm> builder)
    {
        builder.ToTable("Firms");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();

        builder.Property(f => f.Name)
            .HasMaxLength(Firm.MaxNameLength)
            .IsRequired();

        // Two columns of the firm's row (AutoApprovalEnabled, AutoApprovalMinConfidence). The defaults are what the
        // firms that existed before it get: off
        builder.ComplexProperty(f => f.AutoApproval, policy =>
        {
            policy.Property(p => p.Enabled)
                .HasColumnName("AutoApprovalEnabled")
                .HasDefaultValue(false);
            policy.Property(p => p.MinConfidence)
                .HasColumnName("AutoApprovalMinConfidence")
                .HasDefaultValue(AutoApprovalPolicy.DefaultMinConfidence);
        });

        // Four columns of the firm's row. Plan and status as text, like the roles: readable, and safe to reorder
        builder.ComplexProperty(f => f.Subscription, subscription =>
        {
            subscription.Property(s => s.Plan)
                .HasColumnName("Plan")
                .HasConversion<string>()
                .HasMaxLength(EnumMaxLength)
                .IsUnicode(false);
            subscription.Property(s => s.Status)
                .HasColumnName("SubscriptionStatus")
                .HasConversion<string>()
                .HasMaxLength(EnumMaxLength)
                .IsUnicode(false);
            subscription.Property(s => s.PeriodStart).HasColumnName("SubscriptionPeriodStart");
            subscription.Property(s => s.PeriodEnd).HasColumnName("SubscriptionPeriodEnd");
        });

        // The firm owns its users: they are saved with it and go away with it
        builder.HasMany(f => f.Users)
            .WithOne()
            .HasForeignKey(u => u.FirmId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(f => f.Users).HasField("_users");

        // Same for its invitations
        builder.HasMany(f => f.Invitations)
            .WithOne()
            .HasForeignKey(i => i.FirmId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(f => f.Invitations).HasField("_invitations");
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

        // Copies of the Entra profile, null for users who haven't signed in since they were added
        builder.Property(u => u.Name)
            .HasMaxLength(UserProfile.MaxNameLength);

        builder.Property(u => u.Email)
            .HasConversion(email => email!.Value, value => EmailAddress.Create(value))
            .HasMaxLength(EmailAddress.MaxLength);

        // Derived from Name and Email
        builder.Ignore(u => u.Profile);
    }
}

internal sealed class FirmInvitationConfiguration : IEntityTypeConfiguration<FirmInvitation>
{
    public void Configure(EntityTypeBuilder<FirmInvitation> builder)
    {
        builder.ToTable("FirmInvitations");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        // Unicode: addresses may have non-ASCII characters (RFC 6531)
        builder.Property(i => i.Email)
            .HasConversion(email => email.Value, value => EmailAddress.Create(value))
            .HasMaxLength(EmailAddress.MaxLength)
            .IsRequired();

        // At most one open invitation per address and firm, even if the owner double-clicks "Invite" (Firm.Invite
        // renews the open one; this catches two requests racing). Email first, so the same index answers
        // "which firms invited me?" on every /api/me of a user without a firm
        builder.HasIndex(i => new { i.Email, i.FirmId })
            .IsUnique()
            .HasFilter("[AcceptedAt] IS NULL AND [RevokedAt] IS NULL");
    }
}
