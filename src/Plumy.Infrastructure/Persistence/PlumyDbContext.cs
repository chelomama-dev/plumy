using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Plumy.Application.Abstractions;
using Plumy.Application.Common;
using Plumy.Domain.ClientCompanies;
using Plumy.Domain.Firms;
using Plumy.Domain.Invoices;
using Plumy.Infrastructure.Persistence.Configurations;

namespace Plumy.Infrastructure.Persistence;

public sealed class PlumyDbContext(DbContextOptions<PlumyDbContext> options, ITenantContext tenant)
    : DbContext(options), IUnitOfWork
{
    // SQL Server error numbers for a duplicate key in a unique index / unique constraint
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public DbSet<ClientCompany> ClientCompanies => Set<ClientCompany>();
    public DbSet<Firm> Firms => Set<Firm>();
    public DbSet<FirmUser> FirmUsers => Set<FirmUser>();
    public DbSet<FirmInvitation> FirmInvitations => Set<FirmInvitation>();
    public DbSet<Invoice> Invoices => Set<Invoice>();

    // Read by the tenant query filters on every query: EF Core turns it into a parameter of this instance.
    // It must be a member of the DbContext, not a captured variable, or the first firm would be baked into the model
    internal Guid CurrentFirmId => tenant.FirmId;

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException
                                           {
                                               Number: UniqueIndexViolation or UniqueConstraintViolation
                                           })
        {
            throw new ConflictException("The record conflicts with an existing one.");
        }
    }

    // Listed by hand: ApplyConfigurationsFromAssembly silently skips configurations whose constructor takes
    // arguments, and the tenant-scoped configurations need this context for their filter
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new FirmConfiguration());
        modelBuilder.ApplyConfiguration(new FirmUserConfiguration());
        modelBuilder.ApplyConfiguration(new FirmInvitationConfiguration());
        modelBuilder.ApplyConfiguration(new ClientCompanyConfiguration(this));
        modelBuilder.ApplyConfiguration(new InvoiceConfiguration(this));
    }
}
