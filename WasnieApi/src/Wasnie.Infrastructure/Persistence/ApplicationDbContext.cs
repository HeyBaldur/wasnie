using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;
using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Quotas;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Identity;
using Wasnie.Infrastructure.Persistence.Configurations;
using Wasnie.Infrastructure.Persistence.Configurations.Compensation;
using Wasnie.Infrastructure.Persistence.Configurations.Identity;
using LegacyPlan = Wasnie.Domain.Entities.Plan;
using LegacyTransaction = Wasnie.Domain.Entities.Transaction;
using LegacyPayout = Wasnie.Domain.Entities.Payout;

namespace Wasnie.Infrastructure.Persistence;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ITenantContext tenantContext,
    IPublisher publisher)
    : IdentityDbContext<IdentityUser>(options), IApplicationDbContext
{
    public Guid CurrentTenantId { get; } = tenantContext.TenantId;
    public Microsoft.EntityFrameworkCore.DbSet<Wasnie.Domain.Entities.Tenant> Tenants => Set<Wasnie.Domain.Entities.Tenant>();
    public Microsoft.EntityFrameworkCore.DbSet<Wasnie.Domain.Entities.ImportAudit> ImportAudits => Set<Wasnie.Domain.Entities.ImportAudit>();
    public Microsoft.EntityFrameworkCore.DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public Microsoft.EntityFrameworkCore.DbSet<Payee> Payees => Set<Payee>();
    public Microsoft.EntityFrameworkCore.DbSet<LegacyPlan> Plans => Set<LegacyPlan>();
    public Microsoft.EntityFrameworkCore.DbSet<LegacyTransaction> Transactions => Set<LegacyTransaction>();
    public Microsoft.EntityFrameworkCore.DbSet<LegacyPayout> Payouts => Set<LegacyPayout>();
    public Microsoft.EntityFrameworkCore.DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public Microsoft.EntityFrameworkCore.DbSet<Plan> CompensationPlans => Set<Plan>();
    public Microsoft.EntityFrameworkCore.DbSet<Quota> Quotas => Set<Quota>();
    public Microsoft.EntityFrameworkCore.DbSet<PlanAssignment> PlanAssignments => Set<PlanAssignment>();
    public Microsoft.EntityFrameworkCore.DbSet<CompensationTransaction> CompensationTransactions => Set<CompensationTransaction>();
    public Microsoft.EntityFrameworkCore.DbSet<Credit> Credits => Set<Credit>();
    public Microsoft.EntityFrameworkCore.DbSet<CompensationPayout> CompensationPayouts => Set<CompensationPayout>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfiguration(new TenantConfiguration());
        builder.ApplyConfiguration(new PayeeConfiguration());
        builder.ApplyConfiguration(new PlanConfiguration());
        builder.ApplyConfiguration(new TransactionConfiguration());
        builder.ApplyConfiguration(new PayoutConfiguration());
        builder.ApplyConfiguration(new RefreshTokenConfiguration());

        builder.ApplyConfiguration(new CompensationPlanConfiguration());
        builder.ApplyConfiguration(new PlanRuleConfiguration());
        builder.ApplyConfiguration(new QuotaConfiguration());
        builder.ApplyConfiguration(new PlanAssignmentConfiguration());
        builder.ApplyConfiguration(new CompensationTransactionConfiguration());
        builder.ApplyConfiguration(new CreditConfiguration());
        builder.ApplyConfiguration(new CompensationPayoutConfiguration());
        builder.ApplyConfiguration(new PayoutLineConfiguration());
        builder.ApplyConfiguration(new ImportAuditConfiguration());
        builder.ApplyConfiguration(new AuditLogConfiguration());

        builder.Entity<Payee>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<LegacyPlan>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<LegacyTransaction>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<LegacyPayout>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<Plan>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<Quota>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<PlanAssignment>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<CompensationTransaction>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<Credit>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<CompensationPayout>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<Wasnie.Domain.Entities.ImportAudit>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<AuditLog>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var result = await base.SaveChangesAsync(cancellationToken);
        await DispatchDomainEventsAsync(cancellationToken);
        return result;
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        var aggregates = ChangeTracker.Entries<AggregateRoot>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        var events = aggregates.SelectMany(a => a.DomainEvents).ToList();

        foreach (var aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }

        foreach (var @event in events)
        {
            await publisher.Publish(@event, cancellationToken);
        }
    }
}
