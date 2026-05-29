using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Hangfire;
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

        options.AddFixedWindowLimiter("auth-login", o =>
        {
            o.PermitLimit = builder.Configuration.GetValue<int>("RateLimiting:AuthLogin:PermitLimit", 5);
            o.Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("RateLimiting:AuthLogin:WindowSeconds", 60));
            o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            o.QueueLimit = 0;
        });

        options.AddFixedWindowLimiter("auth-register", o =>
        {
            o.PermitLimit = builder.Configuration.GetValue<int>("RateLimiting:AuthRegister:PermitLimit", 3);
            o.Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("RateLimiting:AuthRegister:WindowSeconds", 60));
            o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            o.QueueLimit = 0;
        });

        options.AddFixedWindowLimiter("auth-refresh", o =>
        {
            o.PermitLimit = builder.Configuration.GetValue<int>("RateLimiting:AuthRefresh:PermitLimit", 10);
            o.Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("RateLimiting:AuthRefresh:WindowSeconds", 60));
            o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            o.QueueLimit = 0;
        });

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
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Wasnie API v1"));
    }

    app.UseCors("WasnieUi");
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    // Hangfire dashboard — admin-only. See HangfireDashboardAuthorizationFilter:
    // development only until a global SystemAdmin role is added (cross-tenant data exposure risk).
    app.UseHangfireDashboard("/jobs", new DashboardOptions
    {
        Authorization = [new HangfireDashboardAuthorizationFilter()],
    });

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
