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
    /// <summary>
    /// ★★ AN ENVIRONMENT VARIABLE, AND IT HAS TO BE — <c>ConfigureAppConfiguration</c> IS TOO LATE.
    ///
    /// The test host is an environment, so it states its AI provider like every other one: there is no
    /// default, and Infrastructure refuses to start without one, because that key decides which
    /// third-party vendor receives conversation data. No API key is configured anywhere here, so
    /// nothing is ever sent — the provider resolves and reports itself unconfigured.
    ///
    /// ★ WHY NOT THE IN-MEMORY COLLECTION BELOW, WHICH IS WHERE THIS OBVIOUSLY BELONGS. It was put
    /// there first and it did nothing, and the whole integration suite — 616 tests — failed with
    /// "The entry point exited without ever building an IHost", which names neither the key nor the
    /// guard. The ordering is the reason: <c>Program.cs</c> calls
    /// <c>AddInfrastructure(builder.Configuration)</c> in its TOP-LEVEL STATEMENTS, before
    /// <c>builder.Build()</c> — and the callbacks this factory registers are replayed onto the real
    /// builder only when <c>Build()</c> is intercepted. So the guard read the configuration a moment
    /// before this factory's copy of it existed, threw, and Program's own try/catch swallowed the
    /// message into the log where the test runner never showed it.
    ///
    /// Environment variables are different because they are a source <c>WebApplication.CreateBuilder</c>
    /// reads while it is being constructed — i.e. before the first line of Program.cs after it. This is
    /// the only channel that reaches configuration read that early.
    ///
    /// ★ A STATIC INITIALISER, so it runs at type load — which is <c>new TestWebApplicationFactory(…)</c>,
    /// one line before the fixture touches <c>.Services</c> and starts the host. It sets a variable on
    /// the test process, which is the intended blast radius: the process exists to run these tests.
    /// </summary>
    private static readonly bool ProviderDeclared = DeclareAssistantProvider();

    private static bool DeclareAssistantProvider()
    {
        Environment.SetEnvironmentVariable("Assistant__Provider", "OpenRouter");
        return true;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Touched so the static initialiser above cannot be optimised away or deferred past the host
        // build. It is the one line that makes the difference between a running suite and 616 failures.
        _ = ProviderDeclared;

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
                // ★ "Assistant:Provider" IS DELIBERATELY NOT HERE — see the static initialiser above.
                // This collection is applied when the host is BUILT, and the guard that reads that key
                // runs before that. Putting it back here would look right, change nothing, and break
                // every integration test with an error that names neither the key nor the guard.
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
