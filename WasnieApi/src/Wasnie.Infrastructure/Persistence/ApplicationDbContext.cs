using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Domain.Audit;
using Wasnie.Domain.BackgroundJobs;
using Wasnie.Domain.Common;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.Plans;
using Wasnie.Domain.Compensation.Quotas;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Identity;
using Wasnie.Domain.Integrations.HubSpot;
using Wasnie.Domain.Settings;
using Wasnie.Domain.Subscription;
using Wasnie.Infrastructure.Persistence.Configurations;
using Wasnie.Infrastructure.Persistence.Configurations.BackgroundJobs;
using Wasnie.Infrastructure.Persistence.Configurations.Compensation;
using Wasnie.Infrastructure.Persistence.Configurations.Identity;
using Wasnie.Infrastructure.Persistence.Configurations.Integrations;
using LegacyPayout = Wasnie.Domain.Entities.Payout;
using LegacyPlan = Wasnie.Domain.Entities.Plan;
using LegacyTransaction = Wasnie.Domain.Entities.Transaction;

namespace Wasnie.Infrastructure.Persistence;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ITenantContext tenantContext,
    IPublisher publisher)
    : IdentityDbContext<IdentityUser>(options), IApplicationDbContext
{
    // Evaluated per-query (not at construction) so background jobs can set tenant before first DB access.
    public Guid CurrentTenantId => tenantContext.TenantId;

    public Microsoft.EntityFrameworkCore.DbSet<BackgroundJobRecord> BackgroundJobRecords => Set<BackgroundJobRecord>();
    public Microsoft.EntityFrameworkCore.DbSet<Wasnie.Domain.Entities.Tenant> Tenants => Set<Wasnie.Domain.Entities.Tenant>();
    public Microsoft.EntityFrameworkCore.DbSet<FieldRequirementSetting> FieldRequirementSettings => Set<FieldRequirementSetting>();
    public Microsoft.EntityFrameworkCore.DbSet<Wasnie.Domain.Entities.ImportAudit> ImportAudits => Set<Wasnie.Domain.Entities.ImportAudit>();
    public Microsoft.EntityFrameworkCore.DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public Microsoft.EntityFrameworkCore.DbSet<Payee> Payees => Set<Payee>();
    public Microsoft.EntityFrameworkCore.DbSet<LegacyPlan> Plans => Set<LegacyPlan>();
    public Microsoft.EntityFrameworkCore.DbSet<LegacyTransaction> Transactions => Set<LegacyTransaction>();
    public Microsoft.EntityFrameworkCore.DbSet<LegacyPayout> Payouts => Set<LegacyPayout>();
    public Microsoft.EntityFrameworkCore.DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public Microsoft.EntityFrameworkCore.DbSet<EmailConfirmationToken> EmailConfirmationTokens => Set<EmailConfirmationToken>();
    public Microsoft.EntityFrameworkCore.DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public Microsoft.EntityFrameworkCore.DbSet<EmailChangeToken> EmailChangeTokens => Set<EmailChangeToken>();

    public Microsoft.EntityFrameworkCore.DbSet<Plan> CompensationPlans => Set<Plan>();
    public Microsoft.EntityFrameworkCore.DbSet<Quota> Quotas => Set<Quota>();
    public Microsoft.EntityFrameworkCore.DbSet<PlanAssignment> PlanAssignments => Set<PlanAssignment>();
    public Microsoft.EntityFrameworkCore.DbSet<CompensationTransaction> CompensationTransactions => Set<CompensationTransaction>();
    public Microsoft.EntityFrameworkCore.DbSet<Wasnie.Domain.Compensation.Enrichment.CategoryMapping> CategoryMappings => Set<Wasnie.Domain.Compensation.Enrichment.CategoryMapping>();
    public Microsoft.EntityFrameworkCore.DbSet<Credit> Credits => Set<Credit>();
    public Microsoft.EntityFrameworkCore.DbSet<CompensationPayout> CompensationPayouts => Set<CompensationPayout>();
    public Microsoft.EntityFrameworkCore.DbSet<PayRun> PayRuns => Set<PayRun>();
    public Microsoft.EntityFrameworkCore.DbSet<PayeeLedgerEntry> PayeeLedgerEntries => Set<PayeeLedgerEntry>();
    public Microsoft.EntityFrameworkCore.DbSet<PayeeBalance> PayeeBalances => Set<PayeeBalance>();
    public Microsoft.EntityFrameworkCore.DbSet<PayRunSettlement> PayRunSettlements => Set<PayRunSettlement>();
    public Microsoft.EntityFrameworkCore.DbSet<UserSubscription> UserSubscriptions => Set<UserSubscription>();
    public Microsoft.EntityFrameworkCore.DbSet<ProcessedStripeEvent> ProcessedStripeEvents => Set<ProcessedStripeEvent>();

    public Microsoft.EntityFrameworkCore.DbSet<HubSpotConnection> HubSpotConnections => Set<HubSpotConnection>();
    public Microsoft.EntityFrameworkCore.DbSet<HubSpotOAuthState> HubSpotOAuthStates => Set<HubSpotOAuthState>();
    public Microsoft.EntityFrameworkCore.DbSet<Wasnie.Domain.Integrations.Crm.CrmOwnerMapping> CrmOwnerMappings => Set<Wasnie.Domain.Integrations.Crm.CrmOwnerMapping>();
    public Microsoft.EntityFrameworkCore.DbSet<Wasnie.Domain.Integrations.Crm.CrmDriftAlert> CrmDriftAlerts => Set<Wasnie.Domain.Integrations.Crm.CrmDriftAlert>();
    public Microsoft.EntityFrameworkCore.DbSet<Wasnie.Domain.Integrations.Crm.DealLostAlert> DealLostAlerts => Set<Wasnie.Domain.Integrations.Crm.DealLostAlert>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfiguration(new TenantConfiguration());
        builder.ApplyConfiguration(new PayeeConfiguration());
        builder.ApplyConfiguration(new FieldRequirementSettingConfiguration());
        builder.ApplyConfiguration(new PlanConfiguration());
        builder.ApplyConfiguration(new TransactionConfiguration());
        builder.ApplyConfiguration(new PayoutConfiguration());
        builder.ApplyConfiguration(new RefreshTokenConfiguration());
        builder.ApplyConfiguration(new EmailConfirmationTokenConfiguration());
        builder.ApplyConfiguration(new PasswordResetTokenConfiguration());
        builder.ApplyConfiguration(new EmailChangeTokenConfiguration());

        builder.ApplyConfiguration(new CompensationPlanConfiguration());
        builder.ApplyConfiguration(new PlanRuleConfiguration());
        builder.ApplyConfiguration(new QuotaConfiguration());
        builder.ApplyConfiguration(new PlanAssignmentConfiguration());
        builder.ApplyConfiguration(new CompensationTransactionConfiguration());
        builder.ApplyConfiguration(new CategoryMappingConfiguration());
        builder.ApplyConfiguration(new CreditConfiguration());
        builder.ApplyConfiguration(new CompensationPayoutConfiguration());
        builder.ApplyConfiguration(new PayoutLineConfiguration());
        builder.ApplyConfiguration(new PayRunConfiguration());
        builder.ApplyConfiguration(new PayeeLedgerEntryConfiguration());
        builder.ApplyConfiguration(new PayeeBalanceConfiguration());
        builder.ApplyConfiguration(new PayRunSettlementConfiguration());
        builder.ApplyConfiguration(new ImportAuditConfiguration());
        builder.ApplyConfiguration(new AuditLogConfiguration());
        builder.ApplyConfiguration(new BackgroundJobRecordConfiguration());
        builder.ApplyConfiguration(new UserSubscriptionConfiguration());
        builder.ApplyConfiguration(new ProcessedStripeEventConfiguration());
        builder.ApplyConfiguration(new HubSpotConnectionConfiguration());
        builder.ApplyConfiguration(new HubSpotOAuthStateConfiguration());
        builder.ApplyConfiguration(new CrmOwnerMappingConfiguration());
        builder.ApplyConfiguration(new CrmDriftAlertConfiguration());
        builder.ApplyConfiguration(new DealLostAlertConfiguration());

        builder.Entity<Payee>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<FieldRequirementSetting>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<LegacyPlan>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<LegacyTransaction>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<LegacyPayout>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<Plan>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<Quota>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<PlanAssignment>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<CompensationTransaction>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<Wasnie.Domain.Compensation.Enrichment.CategoryMapping>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<Credit>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<CompensationPayout>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<PayRun>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<PayeeLedgerEntry>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<PayeeBalance>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<PayRunSettlement>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<Wasnie.Domain.Entities.ImportAudit>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<AuditLog>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<BackgroundJobRecord>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<UserSubscription>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        // HubSpotConnection is tenant-filtered for normal (authenticated) access. HubSpotOAuthState is
        // intentionally NOT filtered — the anonymous OAuth callback resolves the tenant from the state row.
        builder.Entity<HubSpotConnection>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<Wasnie.Domain.Integrations.Crm.CrmOwnerMapping>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<Wasnie.Domain.Integrations.Crm.CrmDriftAlert>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<Wasnie.Domain.Integrations.Crm.DealLostAlert>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
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
