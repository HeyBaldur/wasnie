using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Compensation.Calculation;
using Wasnie.Application.Models.Calculation;
using Wasnie.Application.Models.Imports;
using Wasnie.Application.Services.Imports;
using Wasnie.Infrastructure.BackgroundJobs;
using Wasnie.Infrastructure.Common;
using Wasnie.Infrastructure.Compensation.Calculation;
using Wasnie.Infrastructure.Identity;
using Wasnie.Infrastructure.Observability;
using Wasnie.Infrastructure.Persistence;
using Wasnie.Infrastructure.Services;
using Wasnie.Infrastructure.Services.Audit;
using Wasnie.Infrastructure.Services.Email;
using Wasnie.Infrastructure.Services.HubSpot;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Infrastructure.Integrations.Groq;
using Wasnie.Infrastructure.Integrations.OpenRouter;
using Wasnie.Application.Manual;
using Wasnie.Infrastructure.Services.Assistant;
using Wasnie.Infrastructure.Services.Manual;
using Wasnie.Infrastructure.Services.Imports;

namespace Wasnie.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpContextAccessor();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IGuidGenerator, SystemGuidGenerator>();
        services.AddScoped<ICorrelationIdAccessor, CorrelationIdAccessor>();

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<ApplicationDbContext>());

        // HTTP requests → TenantContext (reads JWT claims from HttpContext).
        // Background job scopes (no HttpContext) → BackgroundJobTenantContext (SetTenant() must be
        // called by the job dispatcher before any DB access).
        services.AddScoped<TenantContext>();
        services.AddScoped<BackgroundJobTenantContext>();
        services.AddScoped<ITenantContext>(sp =>
        {
            var httpCtx = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
            return httpCtx is not null
                ? sp.GetRequiredService<TenantContext>()
                : (ITenantContext)sp.GetRequiredService<BackgroundJobTenantContext>();
        });
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IClaimsService, ClaimsService>();
        services.AddScoped<IAuthorizationService, AuthorizationService>();
        // The single point that decides assistant access. Registered next to the authorization
        // services because it is answered the same way — and kept separate from them because it is an
        // entitlement (per user, headed for per-seat billing), not a role permission.
        services.AddScoped<IAssistantEntitlement, AssistantEntitlement>();
        services.AddScoped<IPaidPlanGate, PaidPlanGate>();
        services.AddScoped<ITierLimitChecker, TierLimitChecker>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IIdentityService, IdentityService>();

        services.AddOptions<ImportOptions>()
            .Bind(configuration.GetSection(ImportOptions.SectionName))
            .Validate(
                o => o.TransactionMaxRows is > 0 and <= 100_000,
                "Imports:TransactionMaxRows must be between 1 and 100,000.")
            .Validate(
                o => o.PayeeMaxRows is > 0 and <= 100_000,
                "Imports:PayeeMaxRows must be between 1 and 100,000.")
            .ValidateOnStart();

        services.AddOptions<StripeOptions>()
            .Bind(configuration.GetSection(StripeOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.SecretKey),
                "Stripe:SecretKey is required. Set it in appsettings.Development.json (dev) or as an environment variable (prod).")
            .Validate(o => !string.IsNullOrWhiteSpace(o.PublishableKey),
                "Stripe:PublishableKey is required. Set it in appsettings.Development.json (dev) or as an environment variable (prod).")
            .Validate(o => !string.IsNullOrWhiteSpace(o.WebhookSecret),
                "Stripe:WebhookSecret is required. Run 'stripe listen' to get your whsec_ and add it to appsettings.Development.json.")
            .ValidateOnStart();

        services.AddOptions<ResendOptions>()
            .Bind(configuration.GetSection(ResendOptions.SectionName));

        services.AddHttpClient("Resend");
        services.AddScoped<IEmailService, ResendEmailService>();

        services.AddScoped<ISubscriptionPlanService, StripeSubscriptionPlanService>();
        services.AddScoped<IStripeCheckoutService, StripeCheckoutService>();
        services.AddScoped<IStripeWebhookService, StripeWebhookService>();
        services.AddScoped<IStripeSubscriptionManagementService, StripeSubscriptionManagementService>();

        // HubSpot OAuth integration (Phase 1). Options are bound WITHOUT ValidateOnStart so the app still
        // starts before the owner configures HubSpot; the endpoints fail gracefully until configured.
        services.AddOptions<HubSpotOptions>()
            .Bind(configuration.GetSection(HubSpotOptions.SectionName));
        // Phase 3 automatic polling sync — cadence/staggering live here (config-only, default hourly).
        services.AddOptions<HubSpotSyncOptions>()
            .Bind(configuration.GetSection(HubSpotSyncOptions.SectionName));
        services.AddHttpClient("HubSpot");

        // Chat model behind the vendor-neutral IChatCompletionProvider. Bound WITHOUT ValidateOnStart,
        // like HubSpot: with no key configured the assistant falls back to its stand-in reply instead of
        // stopping the API from starting. ONE key, server-side, for every tenant — it is attached to the
        // outbound request inside GroqChatProvider and exists nowhere else.
        services.AddOptions<GroqOptions>()
            .Bind(configuration.GetSection(GroqOptions.SectionName));
        services.AddOptions<OpenRouterOptions>()
            .Bind(configuration.GetSection(OpenRouterOptions.SectionName));
        services.AddOptions<AssistantProviderOptions>()
            .Bind(configuration.GetSection(AssistantProviderOptions.SectionName));

        // One named client per vendor: a timeout tuned for one endpoint must not silently apply to the
        // other. Both are registered whichever is selected — an unused HttpClient costs nothing, and
        // conditional registration would make switching provider a restart-and-pray.
        services.AddHttpClient(GroqChatProvider.HttpClientName);
        services.AddHttpClient(OpenRouterChatProvider.HttpClientName);

        // ★ THE CHOICE IS A CONFIGURATION VALUE, AND IT IS MANDATORY. Both providers implement the same
        // interface, so which one answers is a registration detail — switching is an appsettings edit
        // and a restart rather than a deployment.
        //
        // ★ FAIL-CLOSED: NO DEFAULT, AND THE FAILURE IS AT START-UP. This used to end in `else =>
        // Groq`, so an absent or misspelt value silently selected Groq. The justification was that a
        // typo in a chat panel's configuration must not stop the API — but this key is not a feature
        // toggle. It decides which third-party vendor, in which country, receives payroll data. Both
        // options are US-based; only one of them has any privacy configuration reported at all, and
        // neither has a signed DPA (docs/Legal.md §3.1, §5). An environment that never states its
        // choice is not asking for the usual one; it has left the question unanswered, and answering
        // it by omission is how European payroll data reaches an unvetted processor.
        //
        // The check lives HERE, at the resolution itself, rather than as ValidateOnStart on the options
        // — this is the line that actually picks the vendor, so a guard anywhere else could be
        // bypassed by a code path that resolves the provider without going through validation. This
        // runs inside AddInfrastructure (Program.cs:35), i.e. while the host is being built: the
        // deployment finds out, not a user asking about their commission.
        var selected = configuration[$"{AssistantProviderOptions.SectionName}:{nameof(AssistantProviderOptions.Provider)}"];

        if (string.Equals(selected, AssistantProviderOptions.OpenRouter, StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IChatCompletionProvider, OpenRouterChatProvider>();
        }
        else if (string.Equals(selected, AssistantProviderOptions.Groq, StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IChatCompletionProvider, GroqChatProvider>();
        }
        else
        {
            // The message names the key and the admitted values, because the person reading it is
            // looking at a container that will not start and needs to know what to type, not that
            // something was invalid.
            var key = $"{AssistantProviderOptions.SectionName}:{nameof(AssistantProviderOptions.Provider)}";
            var problem = string.IsNullOrWhiteSpace(selected)
                ? $"'{key}' is not configured."
                : $"'{key}' has the unrecognised value '{selected}'.";

            throw new InvalidOperationException(
                $"{problem} It is REQUIRED and has no default: it selects which third-party AI provider " +
                $"receives conversation data, and that choice must never be made by omission. Set it to " +
                $"'{AssistantProviderOptions.OpenRouter}' or '{AssistantProviderOptions.Groq}' in " +
                $"appsettings for this environment, or as the environment variable " +
                $"'{AssistantProviderOptions.SectionName}__{nameof(AssistantProviderOptions.Provider)}'.");
        }
        // The documentation the assistant answers from. SINGLETON because it reads one file once and
        // that file cannot change while the process runs — re-reading fifteen thousand tokens of text
        // per message would be work done for nothing.
        services.AddSingleton<IAssistantKnowledgeBase, FileAssistantKnowledgeBase>();
        // The app's real routes, so the assistant can link instead of describing. Singleton for the same
        // reason and separate from the knowledge base because it is a separate artefact with its own
        // lifecycle — see IUiNavigationMap.
        services.AddSingleton<IUiNavigationMap, FileUiNavigationMap>();
        // The user manual PDF, served ONLY behind the API's authentication — there is no public URL for
        // it anywhere. Bound without ValidateOnStart for the usual reason: an installation that has not
        // received the manual yet must still start, and the screen says so instead of the API refusing
        // to run. Singleton because it caches the bytes after the first successful read.
        services.AddOptions<UserManualOptions>()
            .Bind(configuration.GetSection(UserManualOptions.SectionName));
        services.AddSingleton<IUserManualSource, FileUserManualSource>();
        // Step one of the two-step answer: picks the sections a question needs. Scoped because it
        // depends on the scoped provider; it holds no state of its own.
        services.AddScoped<Wasnie.Application.Assistant.Common.AssistantSectionRouter>();
        // Step 1.5: the read-only lookups. SCOPED, and that is load-bearing — the tool sends the same
        // MediatR queries the screens send, and it must do so inside the ASKING USER'S request scope so
        // the tenant filter and the permission guards see the real caller. A singleton here would be a
        // tool holding somebody else's identity.
        services.AddScoped<IAssistantTool, Wasnie.Application.Assistant.Tools.GetTransactionTool>();
        services.AddScoped<IAssistantTool, Wasnie.Application.Assistant.Tools.GetPlanRulesTool>();
        services.AddScoped<IAssistantTool, Wasnie.Application.Assistant.Tools.GetPayeeLedgerSummaryTool>();
        services.AddScoped<IAssistantTool, Wasnie.Application.Assistant.Tools.GetPayeePlansTool>();
        services.AddScoped<Wasnie.Application.Assistant.Common.AssistantToolRunner>();
        services.AddScoped<ITokenEncryptionService, AesTokenEncryptionService>();
        services.AddScoped<IHubSpotOAuthClient, HubSpotOAuthClient>();
        services.AddScoped<IHubSpotTokenProvider, HubSpotTokenProvider>();
        // Phase 2: deal/owner ingestion behind the CRM-neutral abstraction. HubSpot is one implementation;
        // Salesforce/Pipedrive would register a different ICrmDealSource without touching the pipeline.
        services.AddScoped<Wasnie.Application.Integrations.Crm.ICrmDealSource, HubSpotCrmDealSource>();
        services.AddScoped<Wasnie.Application.Integrations.Crm.ICrmOwnerResolver,
            Wasnie.Infrastructure.Services.Crm.CrmOwnerResolver>();

        // Resource-level authorisation for payee data (BOLA/IDOR guard). SCOPED and never singleton:
        // it caches "which payees may I see" for the duration of ONE request, and that answer belongs
        // to one principal. A singleton would serve the first caller's visibility to everybody after.
        services.AddScoped<IPayeeAccessGuard, Wasnie.Application.Authorization.PayeeAccessGuard>();

        services.AddMemoryCache();
        services.AddScoped<IAuditDispatcher, SyncAuditDispatcher>();
        services.AddScoped<IAuditService, AuditService>();

        services.AddScoped<ICreditAllocationService, CreditAllocationService>();
        // Clawback: withholds a payee's outstanding balance when a pay run is marked Paid.
        services.AddScoped<IPayRunSettlementService, PayRunSettlementService>();
        // Single place for the "can I create this transaction?" rule — used by HubSpot/Excel/Manual ingest.
        services.AddScoped<Wasnie.Application.Compensation.Common.ITransactionCreateGuard,
            Wasnie.Application.Compensation.Common.TransactionCreateGuard>();
        // Enrichment phase (WI-ENRICHMENT): resolves a transaction's Category from the tenant lookup
        // table. Same "written once, invoked by all three ingest origins" shape as the create guard.
        services.AddScoped<Wasnie.Application.Compensation.Enrichment.ITransactionEnrichmentService,
            Wasnie.Application.Compensation.Enrichment.TransactionEnrichmentService>();
        // Single place for the "a CRM deal changed after import — what now?" rule. Used by the manual
        // HubSpot import today and the future polling job (clean architecture; CRM-neutral).
        services.AddScoped<Wasnie.Application.Integrations.Crm.Drift.ICrmDriftPolicy,
            Wasnie.Application.Integrations.Crm.Drift.CrmDriftPolicy>();
        // Single place that materialises CRM deals → transactions (guard + drift). Manual import AND the
        // Phase-3 polling job both call this — the logic is written once, only invoked.
        services.AddScoped<Wasnie.Application.Integrations.Crm.ICrmDealReconciler,
            Wasnie.Application.Integrations.Crm.CrmDealReconciler>();
        // Reverse reconciliation (deal-lost detection): checks already-credited deals' current won-status
        // and raises DealLostAlerts. Runs at the end of each sync; read + alert only, never touches money.
        services.AddScoped<Wasnie.Application.Integrations.Crm.IDealLostReconciler,
            Wasnie.Application.Integrations.Crm.DealLostReconciler>();
        services.AddScoped<IQuotaAttainmentService, QuotaAttainmentService>();
        services.AddScoped<ITransactionExcelExportService, TransactionExcelExportService>();
        services.AddScoped<ICreditExcelExportService, CreditExcelExportService>();
        services.AddScoped<IPayoutPdfExportService, PayoutPdfExportService>();
        services.AddScoped<IPayoutExcelExportService, PayoutExcelExportService>();
        services.AddScoped<IPayRunExcelExportService, PayRunExcelExportService>();
        services.AddScoped<IFieldRequirementService, FieldRequirementService>();
        services.AddScoped<IImportCacheService, ImportCacheService>();
        services.AddScoped<IFileParserService, FileParserService>();
        services.AddScoped<IPayeeImportValidationService, PayeeImportValidationService>();
        services.AddScoped<IPayeeImportExecutionService, PayeeImportExecutionService>();
        services.AddScoped<ITransactionImportValidationService, TransactionImportValidationService>();
        services.AddScoped<ITransactionUpdateValidationService, TransactionUpdateValidationService>();

        // Background jobs — Hangfire (LGPLv3) backed by the existing Azure SQL database.
        // F1 plan has no Always On; the Hangfire server restarts on the next request after idle,
        // and SQL-backed jobs survive the recycle. Upgrade to B1 on first paying customer.
        var hangfireConnStr = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is required for Hangfire storage.");

        services.AddHangfire(cfg => cfg
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(hangfireConnStr, new SqlServerStorageOptions
            {
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.Zero,
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true,
            })
            .UseFilter(new Hangfire.AutomaticRetryAttribute { Attempts = 3 }));

        services.AddHangfireServer();

        services.AddScoped<HangfireJobDispatcher>();
        services.AddScoped<IBackgroundJobService, HangfireBackgroundJobService>();

        // Phase 3 HubSpot polling sync: recurring orchestrator + the per-tenant worker it fans out to.
        services.AddScoped<HubSpotSyncOrchestrator>();
        services.AddScoped<HubSpotTenantSyncJob>();
        // "Sync now" enqueues the same per-tenant job on demand (Hangfire behind an Application abstraction).
        services.AddScoped<Wasnie.Application.Integrations.Crm.ICrmSyncScheduler, HangfireCrmSyncScheduler>();

        // Register job handlers so the dispatcher can resolve them by interface type.
        services.AddScoped<IJobHandler<PingPayload>, PingJobHandler>();
        services.AddScoped<IJobHandler<TransactionImportPayload>, TransactionImportJobHandler>();
        services.AddScoped<IJobHandler<ProcessPendingTransactionsPayload>, ProcessPendingTransactionsJobHandler>();
        services.AddScoped<IJobHandler<TransactionUpdatePayload>, UpdateTransactionsFromExcelJobHandler>();
        services.AddScoped<IJobHandler<CalculatePayoutsPayload>, CalculatePayoutsJobHandler>();

        services.AddIdentity<IdentityUser, IdentityRole>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredLength = 10;
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = false;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        return services;
    }
}
