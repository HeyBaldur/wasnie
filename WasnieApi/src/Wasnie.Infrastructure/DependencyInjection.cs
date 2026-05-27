using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Services.Imports;
using Wasnie.Infrastructure.Common;
using Wasnie.Infrastructure.Identity;
using Wasnie.Infrastructure.Observability;
using Wasnie.Infrastructure.Persistence;
using Wasnie.Infrastructure.Services;
using Wasnie.Infrastructure.Services.Audit;
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

        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IClaimsService, ClaimsService>();
        services.AddScoped<IAuthorizationService, AuthorizationService>();
        services.AddScoped<ITierLimitChecker, TierLimitChecker>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IIdentityService, IdentityService>();

        services.AddMemoryCache();
        services.AddScoped<IAuditDispatcher, SyncAuditDispatcher>();
        services.AddScoped<IAuditService, AuditService>();

        services.AddScoped<IImportCacheService, ImportCacheService>();
        services.AddScoped<IFileParserService, FileParserService>();
        services.AddScoped<IPayeeImportValidationService, PayeeImportValidationService>();
        services.AddScoped<IPayeeImportExecutionService, PayeeImportExecutionService>();

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
