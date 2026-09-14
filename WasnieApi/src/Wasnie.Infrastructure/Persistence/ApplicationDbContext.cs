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

/// <remarks>
/// ★★ DEJÓ DE SER `sealed` PARA QUE EL SANDBOX PUEDA HEREDARLO (KAN-68), Y ESA ES LA ÚNICA RAZÓN.
/// El onboarding guiado necesita que la MISMA lógica escriba en otro juego de tablas. Heredar es lo
/// que evita duplicar el modelo: `SandboxDbContext` no redefine ni una configuración — sólo declara
/// otro esquema. Un segundo contexto escrito a mano habría que mantenerlo en paralelo para siempre, y
/// el día que se olvidara una tabla el onboarding enseñaría algo que el producto ya no hace.
///
/// ★★ DOS CONSTRUCTORES, Y NO ES UN CAPRICHO: LO EXIGE EF Y LO DESCUBRIÓ EL ARRANQUE. Con un único
/// constructor que aceptara `DbContextOptions` a secas, el contenedor inyectaba aquí las opciones del
/// OTRO contexto — `AddDbContext` registra también el tipo no genérico, y con dos contextos gana el
/// último registrado. EF lo detecta y se niega a arrancar: «The DbContextOptions passed to the
/// ApplicationDbContext constructor must be a DbContextOptions&lt;ApplicationDbContext&gt;».
///
/// El público lleva el tipo exacto, que es lo que resuelve el contenedor sin ambigüedad; el protegido
/// existe sólo para que el heredero pueda pasar las SUYAS. Compilaba igual de bien de las dos formas:
/// la diferencia sólo aparece al levantar la aplicación.
/// </remarks>
public class ApplicationDbContext : IdentityDbContext<IdentityUser>, IApplicationDbContext
{
    private readonly ITenantContext tenantContext;
    private readonly IPublisher publisher;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ITenantContext tenantContext,
        IPublisher publisher)
        : base(options)
    {
        this.tenantContext = tenantContext;
        this.publisher = publisher;
    }

    /// <summary>Para los contextos derivados, que traen sus propias opciones tipadas.</summary>
    protected ApplicationDbContext(
        DbContextOptions options,
        ITenantContext tenantContext,
        IPublisher publisher)
        : base(options)
    {
        this.tenantContext = tenantContext;
        this.publisher = publisher;
    }

    /// <summary>
    /// El esquema donde viven las tablas del ciclo de compensación. `null` = el esquema por omisión.
    ///
    /// ★★ ES LA FRONTERA ENTRE EL DINERO REAL Y EL DE PRUEBA, Y ES ESTRUCTURAL. Un pay run real
    /// consulta `Credits`; los créditos del onboarding están en `Sandbox.Credits`. No hay filtro que
    /// alguien pueda olvidarse de poner — la separación la hace el motor de base de datos, no una
    /// convención que hay que recordar en cada consulta (§B5: la barrera no se desincroniza).
    /// </summary>
    protected virtual string? CompensationSchema => null;
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

    public Microsoft.EntityFrameworkCore.DbSet<Wasnie.Domain.Assistant.AssistantConversation> AssistantConversations => Set<Wasnie.Domain.Assistant.AssistantConversation>();
    public Microsoft.EntityFrameworkCore.DbSet<Wasnie.Domain.Assistant.AssistantMessage> AssistantMessages => Set<Wasnie.Domain.Assistant.AssistantMessage>();
    public Microsoft.EntityFrameworkCore.DbSet<Wasnie.Domain.Assistant.AssistantConversationState> AssistantConversationStates => Set<Wasnie.Domain.Assistant.AssistantConversationState>();

    public Microsoft.EntityFrameworkCore.DbSet<HubSpotConnection> HubSpotConnections => Set<HubSpotConnection>();
    public Microsoft.EntityFrameworkCore.DbSet<HubSpotOAuthState> HubSpotOAuthStates => Set<HubSpotOAuthState>();
    public Microsoft.EntityFrameworkCore.DbSet<Wasnie.Domain.Integrations.Crm.CrmOwnerMapping> CrmOwnerMappings => Set<Wasnie.Domain.Integrations.Crm.CrmOwnerMapping>();
    public Microsoft.EntityFrameworkCore.DbSet<Wasnie.Domain.Integrations.Crm.CrmDriftAlert> CrmDriftAlerts => Set<Wasnie.Domain.Integrations.Crm.CrmDriftAlert>();
    public Microsoft.EntityFrameworkCore.DbSet<Wasnie.Domain.Integrations.Crm.DealLostAlert> DealLostAlerts => Set<Wasnie.Domain.Integrations.Crm.DealLostAlert>();
    public Microsoft.EntityFrameworkCore.DbSet<Wasnie.Domain.Compensation.Reconciliation.ReconciliationClosure> ReconciliationClosures => Set<Wasnie.Domain.Compensation.Reconciliation.ReconciliationClosure>();

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
        builder.ApplyConfiguration(new ReconciliationClosureConfiguration());
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
        builder.ApplyConfiguration(new Configurations.Assistant.AssistantConversationConfiguration());
        builder.ApplyConfiguration(new Configurations.Assistant.AssistantMessageConfiguration());
        builder.ApplyConfiguration(new Configurations.Assistant.AssistantConversationStateConfiguration());
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
        builder.Entity<Wasnie.Domain.Compensation.Reconciliation.ReconciliationClosure>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<BackgroundJobRecord>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<UserSubscription>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        // Assistant chat. The tenant filter is a FLOOR, not the isolation: a conversation also belongs
        // to one user, and every query adds `UserId == currentUser` on top of this. A query filter
        // cannot express the user half — ITenantContext knows the tenant, not the principal — so the
        // handlers carry it, and the isolation test is what keeps them honest.
        builder.Entity<Wasnie.Domain.Assistant.AssistantConversation>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<Wasnie.Domain.Assistant.AssistantMessage>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        // Same floor, same reason: a standing belongs to one USER, and the handlers add that half.
        builder.Entity<Wasnie.Domain.Assistant.AssistantConversationState>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        // HubSpotConnection is tenant-filtered for normal (authenticated) access. HubSpotOAuthState is
        // intentionally NOT filtered — the anonymous OAuth callback resolves the tenant from the state row.
        builder.Entity<HubSpotConnection>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<Wasnie.Domain.Integrations.Crm.CrmOwnerMapping>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<Wasnie.Domain.Integrations.Crm.CrmDriftAlert>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        builder.Entity<Wasnie.Domain.Integrations.Crm.DealLostAlert>().HasQueryFilter(e => e.TenantId == CurrentTenantId);

        // ★★ EL HISTORIAL DE EXPERIMENTOS SÓLO EXISTE EN EL SANDBOX. Se configura dentro de este `if` y no
        // fuera: mapearlo siempre creaba una tabla `Experiments` en el esquema real para guardar
        // pruebas que no son de nadie. Lo que es del sandbox vive en el sandbox, también cuando es
        // una tabla suya y no una copia de una real.
        //
        // ★ VA ANTES DEL REPARTO DE ESQUEMAS a propósito: así la vuelta de abajo ya lo ve con su esquema
        // puesto y no lo excluye de las migraciones por no estar en la lista de tablas duplicadas.
        if (!string.IsNullOrWhiteSpace(CompensationSchema))
        {
            builder.ApplyConfiguration(new Configurations.SandboxExperimentConfiguration());
            builder.Entity<Wasnie.Domain.Sandbox.SandboxExperiment>()
                .HasQueryFilter(e => e.TenantId == CurrentTenantId)
                .ToTable("Experiments", CompensationSchema);
        }

        ApplyCompensationSchema(builder);
    }

    /// <summary>
    /// Mueve al esquema del sandbox las tablas del ciclo, y sólo esas.
    /// </summary>
    /// <remarks>
    /// ★★ SE MUEVE EL ESQUEMA, NO SE REDECLARAN LAS TABLAS. Las configuraciones ya dijeron cómo se
    /// llama cada tabla y no declaran esquema; aquí sólo se les cambia el esquema al vuelo. Duplicar las
    /// configuraciones para el sandbox habría creado un segundo juego que se separa del original en
    /// cuanto alguien añada una columna a uno solo.
    ///
    /// ★★ LA LISTA ES LA FRONTERA DE SEGURIDAD, Y POR ESO ES EXPLÍCITA. Lo que NO esté aquí lo comparten
    /// los dos contextos: el tenant, los usuarios, la suscripción, la auditoría. El sandbox tiene que
    /// leer la empresa y la persona de verdad — lo que no puede es escribir dinero de verdad. Mover una
    /// entidad de sitio en esta lista cambia esa frontera: no se toca sin pensarlo.
    ///
    /// Los tipos propiedad (`OwnsMany`/`OwnsOne`, como las líneas de un payout o las reglas de un plan)
    /// siguen a su dueño solos: no hace falta nombrarlos.
    /// </remarks>
    private void ApplyCompensationSchema(ModelBuilder builder)
    {
        var schema = CompensationSchema;
        if (string.IsNullOrWhiteSpace(schema)) return;

        foreach (var clrType in SandboxedEntities)
        {
            builder.Model.FindEntityType(clrType)?.SetSchema(schema);
        }

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var owner = entityType.IsOwned() ? entityType.FindOwnership()?.PrincipalEntityType : null;
            var isSandboxed = SandboxedEntities.Contains(entityType.ClrType)
                || (owner is not null && SandboxedEntities.Contains(owner.ClrType))
                // Lo que ya declaró el esquema del sandbox por su cuenta — el historial de experimentos —
                // también es del sandbox: sin esto quedaba fuera de sus propias migraciones y la tabla no
                // se creaba nunca. La migración salía vacía y en verde.
                || string.Equals(entityType.GetSchema(), schema, StringComparison.Ordinal);

            if (isSandboxed)
            {
                // Un tipo propiedad vive en la tabla de su dueño: se le da el mismo esquema para que la
                // separación no tenga rendijas.
                entityType.SetSchema(schema);
                continue;
            }

            // ★★ LO QUE NO ES DEL SANDBOX QUEDA FUERA DE SUS MIGRACIONES. El modelo del sandbox incluye
            // todo (hereda el del contexto real), pero sus migraciones deben crear ÚNICAMENTE las tablas
            // del esquema Sandbox. Sin esto, la primera migración del sandbox intentaría crear otra vez
            // las tablas de identidad, suscripción y auditoría que ya existen — y no crearía nada, porque
            // fallaría entera.
            entityType.SetIsTableExcludedFromMigrations(true);
        }
    }

    /// <summary>Las entidades del ciclo de compensación: lo único que el sandbox duplica.</summary>
    private static readonly HashSet<Type> SandboxedEntities =
    [
        typeof(Payee),
        typeof(Plan),
        // ★★ LAS HIJAS TAMBIÉN, Y ESTAS DOS FALTABAN. `PlanRule` y `PayoutLine` tienen configuración
        // propia, así que son entidades por derecho y NO las arrastra su padre como haría un tipo
        // propiedad. Sin ellas, la regla de un plan del sandbox se intentaba escribir en la tabla real
        // y chocaba contra su clave foránea: «FK_PlanRules_CompensationPlans_PlanId». Salió al primer
        // intento en pantalla — compilaba, tenía tests en verde y estaba mal.
        typeof(Wasnie.Domain.Compensation.Plans.Rule),
        typeof(Wasnie.Domain.Compensation.Payouts.PayoutLine),
        typeof(Quota),
        typeof(PlanAssignment),
        typeof(CompensationTransaction),
        typeof(Wasnie.Domain.Compensation.Enrichment.CategoryMapping),
        typeof(Credit),
        typeof(CompensationPayout),
        typeof(PayRun),
        typeof(PayeeLedgerEntry),
        typeof(PayeeBalance),
        typeof(PayRunSettlement),
        typeof(Wasnie.Domain.Compensation.Reconciliation.ReconciliationClosure),
    ];

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
