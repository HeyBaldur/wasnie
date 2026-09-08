using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Core;
using Wasnie.Api.Extensions;
using Wasnie.Api.Middleware;
using Wasnie.Api.Observability;
using Wasnie.Application;
using Wasnie.Infrastructure;
using Wasnie.Infrastructure.BackgroundJobs;
using Wasnie.Infrastructure.Persistence.Serialization;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) =>
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services));

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    // Register DI-aware Serilog enricher; picked up automatically by ReadFrom.Services()
    builder.Services.AddSingleton<ILogEventEnricher>(sp =>
        new TenantUserCorrelationEnricher(sp.GetRequiredService<IHttpContextAccessor>()));

    var jwtSettings = builder.Configuration.GetSection("JwtSettings");
    var secret = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret not configured.");

    builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings["Issuer"],
                ValidAudience = jwtSettings["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                ClockSkew = TimeSpan.Zero
            };
        });

    builder.Services.AddAuthorization();
    builder.Services.AddControllers()
        .AddJsonOptions(opts =>
        {
            opts.JsonSerializerOptions.Converters.Add(new MoneyJsonConverter());
            opts.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });
    builder.Services.AddSwaggerWithJwt();

    if (!builder.Environment.IsDevelopment())
    {
        builder.Services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = true;
        });
    }

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.OnRejected = async (ctx, token) =>
        {
            ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ctx.HttpContext.Response.ContentType = "application/json";

            if (ctx.HttpContext.RequestServices.GetService<ILoggerFactory>() is { } lf)
            {
                lf.CreateLogger("RateLimiter").LogWarning(
                    "[DEV] Rate limit exceeded: {Method} {Path} from {IP}",
                    ctx.HttpContext.Request.Method,
                    ctx.HttpContext.Request.Path,
                    ctx.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            }

            await ctx.HttpContext.Response.WriteAsync(
                """{"code":"rate_limited","message":"Too many requests. Please try again later."}""",
                token);
        };

        // ★★ PARTITIONED BY IP, AND THE PREVIOUS SHAPE WAS A DENIAL OF SERVICE ON OUR OWN LOGIN (KAN-21).
        //    These three used AddFixedWindowLimiter, which gives the POLICY one bucket shared by every
        //    caller in the world. Five requests in sixty seconds — one script, or a busy morning — and
        //    nobody could sign in to the product at all. It was also neither of the two things the
        //    acceptance criterion asks for: not per IP, not per account.
        //
        // ★ THE FILE ALREADY KNEW. `auth-resend` below says "Separate from auth-login so an attacker
        //   cannot burn the shared login bucket" — the shared bucket was named as a hazard and worked
        //   around, rather than fixed. These now use the same partitioned shape as its neighbours.
        //
        // ★★ PER-ACCOUNT IS ALREADY COVERED AND IS NOT DUPLICATED HERE. ASP.NET Identity locks an
        //    account after 5 failed attempts for 15 minutes (DependencyInjection.cs:321-323) and the
        //    login path opts in with `lockoutOnFailure: true` (IdentityService.cs:58). Re-implementing
        //    it in the limiter would be a second, competing answer to "is this account under attack",
        //    and the two would drift. IP is the half that was missing; this supplies it.
        options.AddPolicy("auth-login", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = builder.Configuration.GetValue<int>("RateLimiting:AuthLogin:PermitLimit", 5),
                    Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("RateLimiting:AuthLogin:WindowSeconds", 60)),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                }));

        options.AddPolicy("auth-register", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = builder.Configuration.GetValue<int>("RateLimiting:AuthRegister:PermitLimit", 3),
                    Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("RateLimiting:AuthRegister:WindowSeconds", 60)),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                }));

        // ★ REFRESH IS PARTITIONED BY THE CALLER'S IDENTITY WHERE THERE IS ONE. A refresh arrives with an
        //   expired access token, so the principal is usually absent — the IP is then the only honest
        //   key. Falling back rather than branching keeps one bucket per caller either way.
        options.AddPolicy("auth-refresh", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = builder.Configuration.GetValue<int>("RateLimiting:AuthRefresh:PermitLimit", 10),
                    Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("RateLimiting:AuthRefresh:WindowSeconds", 60)),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                }));

        // Resend-confirmation: partitioned by IP (3 requests / 5 minutes per IP).
        // Separate from auth-login so an attacker cannot burn the shared login bucket.
        options.AddPolicy("auth-resend", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = builder.Configuration.GetValue<int>("RateLimiting:AuthResend:PermitLimit", 3),
                    Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("RateLimiting:AuthResend:WindowSeconds", 300)),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                }));

        // ★ EMAIL-CHANGE CONFIRMATION: partitioned by IP (KAN-21). The endpoint is anonymous by
        //   necessity — it is the target of a link in an email — and it carries a token in the query
        //   string, so without a limit the token space is open to being walked. Its own bucket rather
        //   than a shared one: a user confirming an address must not consume the quota that protects
        //   password resets, and vice versa.
        options.AddPolicy("auth-confirm", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = builder.Configuration.GetValue<int>("RateLimiting:AuthConfirm:PermitLimit", 10),
                    Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("RateLimiting:AuthConfirm:WindowSeconds", 300)),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                }));

        // Password-reset: partitioned by IP (3 requests / 5 minutes per IP).
        // Separate bucket so an attacker cannot burn shared login quota.
        options.AddPolicy("auth-password-reset", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = builder.Configuration.GetValue<int>("RateLimiting:AuthPasswordReset:PermitLimit", 3),
                    Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("RateLimiting:AuthPasswordReset:WindowSeconds", 300)),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                }));

        // Profile password change: per-user (authenticated), 5 requests / 5 min.
        options.AddPolicy("profile-password", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                              ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = builder.Configuration.GetValue<int>("RateLimiting:ProfilePassword:PermitLimit", 5),
                    Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("RateLimiting:ProfilePassword:WindowSeconds", 300)),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                }));

        // Profile email change: per-user (authenticated), 3 requests / 5 min.
        options.AddPolicy("profile-email-change", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                              ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = builder.Configuration.GetValue<int>("RateLimiting:ProfileEmailChange:PermitLimit", 3),
                    Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("RateLimiting:ProfileEmailChange:WindowSeconds", 300)),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                }));

        // 2FA verify: per-IP, 5 requests / 15 min — anti brute-force on 6-digit code.
        options.AddPolicy("auth-verify-2fa", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = builder.Configuration.GetValue<int>("RateLimiting:AuthVerify2Fa:PermitLimit", 5),
                    Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("RateLimiting:AuthVerify2Fa:WindowSeconds", 900)),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                }));

        // 2FA profile actions: per-user, 10 requests / 5 min.
        options.AddPolicy("profile-2fa", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                              ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = builder.Configuration.GetValue<int>("RateLimiting:Profile2Fa:PermitLimit", 10),
                    Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("RateLimiting:Profile2Fa:WindowSeconds", 300)),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                }));

        if (!builder.Environment.IsDevelopment())
        {
            var globalPermitLimit = builder.Configuration.GetValue<int>("RateLimiting:Global:PermitLimit", 100);
            var globalWindowSeconds = builder.Configuration.GetValue<int>("RateLimiting:Global:WindowSeconds", 60);

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
                var key = userId ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = globalPermitLimit,
                    Window = TimeSpan.FromSeconds(globalWindowSeconds),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                });
            });
        }
    });

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("WasnieUi", policy =>
            policy
                .WithOrigins(
                    builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
                    ?? ["http://localhost:4200"])
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
    });

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        await Wasnie.Infrastructure.Persistence.DbSeeder.SeedAsync(scope.ServiceProvider);
    }

    app.UseMiddleware<CorrelationIdMiddleware>();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
        app.UseHttpsRedirection();
    }

    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseMiddleware<SecurityHeadersMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Incentra API v1"));
    }

    app.UseCors("WasnieUi");
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<ActivationEnforcementMiddleware>();
    app.UseMiddleware<SubscriptionEnforcementMiddleware>();
    app.UseRateLimiter();

    // Hangfire dashboard — admin-only. See HangfireDashboardAuthorizationFilter:
    // development only until a global SystemAdmin role is added (cross-tenant data exposure risk).
    app.UseHangfireDashboard("/jobs", new DashboardOptions
    {
        Authorization = [new HangfireDashboardAuthorizationFilter()],
    });

    // Phase 3: register (or refresh) the recurring HubSpot incremental-sync orchestrator. The cadence comes
    // from config (HubSpotSync:CronExpression, default hourly), so changing the schedule needs no code edit;
    // restarting the app re-applies it. Disabled → the recurring job is removed.
    using (var syncScope = app.Services.CreateScope())
    {
        var syncOptions = syncScope.ServiceProvider.GetRequiredService<IOptions<HubSpotSyncOptions>>().Value;
        var recurringJobs = syncScope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
        const string hubSpotSyncJobId = "hubspot-incremental-sync";
        if (syncOptions.Enabled)
        {
            recurringJobs.AddOrUpdate<HubSpotSyncOrchestrator>(
                hubSpotSyncJobId,
                o => o.RunAsync(CancellationToken.None),
                syncOptions.CronExpression);
        }
        else
        {
            recurringJobs.RemoveIfExists(hubSpotSyncJobId);
        }
    }

    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application failed to start");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
