using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Wasnie.Domain.Audit;
using Wasnie.Domain.BackgroundJobs;
using Wasnie.Domain.Compensation.Assignments;
using Wasnie.Domain.Compensation.Credits;
using Wasnie.Domain.Compensation.Ledger;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Compensation.Payouts;
using Wasnie.Domain.Compensation.Quotas;
using Wasnie.Domain.Compensation.Transactions;
using Wasnie.Domain.Entities;
using Wasnie.Domain.Identity;
using Wasnie.Domain.Integrations.HubSpot;
using Wasnie.Domain.Settings;
using Wasnie.Domain.Subscription;
using CompensationPlan = Wasnie.Domain.Compensation.Plans.Plan;

namespace Wasnie.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<BackgroundJobRecord> BackgroundJobRecords { get; }
    DbSet<Tenant> Tenants { get; }
    DbSet<FieldRequirementSetting> FieldRequirementSettings { get; }
    DbSet<ImportAudit> ImportAudits { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Payee> Payees { get; }
    DbSet<Plan> Plans { get; }
    DbSet<Transaction> Transactions { get; }
    DbSet<Payout> Payouts { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<EmailConfirmationToken> EmailConfirmationTokens { get; }
    DbSet<PasswordResetToken> PasswordResetTokens { get; }
    DbSet<EmailChangeToken> EmailChangeTokens { get; }

    DbSet<CompensationPlan> CompensationPlans { get; }
    DbSet<Quota> Quotas { get; }
    DbSet<PlanAssignment> PlanAssignments { get; }
    DbSet<CompensationTransaction> CompensationTransactions { get; }
    DbSet<Wasnie.Domain.Compensation.Enrichment.CategoryMapping> CategoryMappings { get; }
    DbSet<Credit> Credits { get; }
    DbSet<CompensationPayout> CompensationPayouts { get; }
    DbSet<PayRun> PayRuns { get; }
    DbSet<PayeeLedgerEntry> PayeeLedgerEntries { get; }
    DbSet<PayeeBalance> PayeeBalances { get; }
    DbSet<PayRunSettlement> PayRunSettlements { get; }
    DbSet<UserSubscription> UserSubscriptions { get; }
    DbSet<ProcessedStripeEvent> ProcessedStripeEvents { get; }

    // Assistant chat. Tenant-filtered like everything else, but note that tenant is only HALF the
    // isolation here: every read must also match the owning UserId (see AssistantConversation).
    DbSet<Wasnie.Domain.Assistant.AssistantConversation> AssistantConversations { get; }
    DbSet<Wasnie.Domain.Assistant.AssistantMessage> AssistantMessages { get; }

    /// <summary>One user's standing on one conversation — pinned, and later archived/read.</summary>
    DbSet<Wasnie.Domain.Assistant.AssistantConversationState> AssistantConversationStates { get; }

    DbSet<HubSpotConnection> HubSpotConnections { get; }
    DbSet<HubSpotOAuthState> HubSpotOAuthStates { get; }
    DbSet<Wasnie.Domain.Integrations.Crm.CrmOwnerMapping> CrmOwnerMappings { get; }
    DbSet<Wasnie.Domain.Integrations.Crm.CrmDriftAlert> CrmDriftAlerts { get; }
    DbSet<Wasnie.Domain.Integrations.Crm.DealLostAlert> DealLostAlerts { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
