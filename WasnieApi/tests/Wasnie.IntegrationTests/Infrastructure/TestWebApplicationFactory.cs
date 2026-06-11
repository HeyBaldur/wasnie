using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Wasnie.Infrastructure.Persistence;

namespace Wasnie.IntegrationTests.Infrastructure;

public sealed class TestWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString, // used by EF + Hangfire
                ["RateLimiting:AuthLogin:PermitLimit"] = "10000",
                ["RateLimiting:AuthLogin:WindowSeconds"] = "60",
                ["RateLimiting:AuthRegister:PermitLimit"] = "10000",
                ["RateLimiting:AuthRegister:WindowSeconds"] = "60",
                ["RateLimiting:AuthRefresh:PermitLimit"] = "10000",
                ["RateLimiting:AuthRefresh:WindowSeconds"] = "60",
                ["RateLimiting:Global:PermitLimit"] = "10000",
                ["RateLimiting:Global:WindowSeconds"] = "60",
                // Stripe test placeholders — real keys are never committed.
                // Integration tests replace ISubscriptionPlanService with a mock.
                ["Stripe:SecretKey"] = "sk_test_integration_test_placeholder",
                ["Stripe:PublishableKey"] = "pk_test_integration_test_placeholder",
                ["Stripe:WebhookSecret"] = TestConstants.StripeWebhookSecret,
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(connectionString));

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.IssuerSigningKey =
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestConstants.JwtSecret));
                options.TokenValidationParameters.ValidIssuer = TestConstants.JwtIssuer;
                options.TokenValidationParameters.ValidAudience = TestConstants.JwtAudience;
            });
        });
    }
}
