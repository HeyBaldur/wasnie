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
        services.AddHttpClient("HubSpot");
        services.AddScoped<ITokenEncryptionService, AesTokenEncryptionService>();
        services.AddScoped<IHubSpotOAuthClient, HubSpotOAuthClient>();
        services.AddScoped<IHubSpotTokenProvider, HubSpotTokenProvider>();

        services.AddMemoryCache();
        services.AddScoped<IAuditDispatcher, SyncAuditDispatcher>();
        services.AddScoped<IAuditService, AuditService>();

        services.AddScoped<ICreditAllocationService, CreditAllocationService>();
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
