using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Wasnie.Application.Common.Options;

namespace Wasnie.UnitTests.Configuration;

/// <summary>
/// The declared configuration matches what the code binds — and carries no secret.
///
/// ★ WHY THIS IS A TEST AND NOT A GLANCE: the section name and every property name are strings on both
/// sides. A rename on either side compiles perfectly and fails silently at runtime, handing the
/// assistant an empty key and the stand-in reply, with nothing in the log to explain it. These read the
/// REAL appsettings.json rather than a fixture, because a fixture would only prove a copy is correct.
/// </summary>
public sealed class GroqConfigurationTests
{
    /// <summary>The committed base settings file, located from the test binary's output directory.</summary>
    private static string AppSettingsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Wasnie.Api")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the WasnieApi folder must be findable from the test output");

        return Path.Combine(dir!.FullName, "src", "Wasnie.Api", "appsettings.json");
    }

    private static IConfigurationRoot LoadAppSettings() =>
        new ConfigurationBuilder().AddJsonFile(AppSettingsPath(), optional: false).Build();

    [Fact]
    public void The_declared_section_binds_onto_GroqOptions_with_the_expected_defaults()
    {
        var configuration = LoadAppSettings();

        var options = new GroqOptions();
        configuration.GetSection(GroqOptions.SectionName).Bind(options);

        // Every non-secret option is declared and lands where the code reads it. If a property is
        // renamed on one side only, the value silently reverts to its C# default and this fails.
        options.BaseUrl.Should().Be("https://api.groq.com/openai/v1");
        options.Model.Should().NotBeNullOrWhiteSpace();
        options.MaxHistoryMessages.Should().BeGreaterThan(0);
        options.TimeoutSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public void The_configured_model_is_the_API_id_not_the_commercial_name()
    {
        // ★ "GPT OSS 20B" is marketing; `openai/gpt-oss-20b` is the string the endpoint answers to,
        // and a wrong one is a 404 on every single call. Verified against GET /v1/models rather than
        // typed from the pricing page.
        var configuration = LoadAppSettings();

        var options = new GroqOptions();
        configuration.GetSection(GroqOptions.SectionName).Bind(options);

        options.Model.Should().Be("openai/gpt-oss-20b");

        // ★ And the C# default must agree with the shipped JSON. They are two places holding the same
        // string, so they can drift — and a drift is invisible: the app would quietly run whichever one
        // happened to win, which is not necessarily the one someone edited.
        new GroqOptions().Model.Should().Be(options.Model,
            "the code default and the declared configuration must name the same model");

        // The shape of an API id, not of a product name: a vendor path, lowercase, no spaces.
        options.Model.Should().Contain("/");
        options.Model.Should().NotContain(" ");
        options.Model.Should().Be(options.Model.ToLowerInvariant());
    }

    [Fact]
    public void The_committed_settings_declare_the_key_but_leave_it_EMPTY()
    {
        var configuration = LoadAppSettings();

        // The field must EXIST — that is what documents the setting and what Azure fills…
        var section = configuration.GetSection(GroqOptions.SectionName);
        section.Exists().Should().BeTrue("the section documents the setting for whoever deploys it");
        section.GetChildren().Select(c => c.Key).Should().Contain(nameof(GroqOptions.ApiKey));

        // …and it must be EMPTY. A committed file cannot hold a credential: removing it later does not
        // remove it from git history, so a leaked key has to be rotated rather than deleted.
        section[nameof(GroqOptions.ApiKey)].Should().BeEmpty();
    }

    [Fact]
    public void No_committed_settings_file_contains_a_Groq_key_value()
    {
        // Structural rather than a spot check on one file: every committed settings file is scanned for
        // the vendor's key prefix.
        var apiDir = Path.GetDirectoryName(AppSettingsPath())!;

        var committed = new[]
        {
            Path.Combine(apiDir, "appsettings.json"),
            Path.Combine(apiDir, "appsettings.Development.template.json"),
        };

        foreach (var file in committed.Where(File.Exists))
        {
            File.ReadAllText(file).Should().NotContain(
                "gsk_", $"{Path.GetFileName(file)} is committed and must never hold a real key");
        }
    }

    [Fact]
    public void The_committed_files_declare_OpenRouter_without_holding_its_key_either()
    {
        // ★ THE SAME SWEEP, FOR THE SECOND VENDOR. A new provider is a new secret and a new chance to
        // paste it into the wrong file — and the wrong file is the one git tracks. OpenRouter keys are
        // prefixed `sk-or-`, so that is what this looks for.
        var apiDir = Path.GetDirectoryName(AppSettingsPath())!;

        var committed = new[]
        {
            Path.Combine(apiDir, "appsettings.json"),
            Path.Combine(apiDir, "appsettings.Development.template.json"),
        };

        foreach (var file in committed.Where(File.Exists))
        {
            var text = File.ReadAllText(file);

            text.Should().Contain(
                OpenRouterOptions.SectionName,
                $"{Path.GetFileName(file)} must declare the section so its shape is reviewable");
            text.Should().NotContain(
                "sk-or-", $"{Path.GetFileName(file)} is committed and must never hold a real key");
        }
    }

    [Fact]
    public void The_OpenRouter_section_binds_with_the_model_id_verified_against_the_catalogue()
    {
        // ★ A wrong model id is a failure on every call, and the same mistake already cost a release on
        // Groq. This one was read from OpenRouter's own GET /api/v1/models, which lists `response_format`
        // and `tools` among its supported parameters — JSON mode for the router, tool calling for the
        // transaction lookup.
        var configuration = new ConfigurationBuilder().AddJsonFile(AppSettingsPath(), optional: false).Build();

        var options = new OpenRouterOptions();
        configuration.GetSection(OpenRouterOptions.SectionName).Bind(options);

        options.Model.Should().Be("openai/gpt-oss-20b");
        options.Model.Should().NotEndWith(":free",
            "the free variant carries the rate limits this provider exists to escape");
        options.BaseUrl.Should().Be("https://openrouter.ai/api/v1");
        options.ApiKey.Should().BeEmpty("the committed file declares the shape, never the secret");

        // The C# default and the declared value must name the same model — a rename in one place only
        // compiles perfectly and fails at runtime.
        new OpenRouterOptions().Model.Should().Be(options.Model);
        new OpenRouterOptions().BaseUrl.Should().Be(options.BaseUrl);
    }

    [Fact]
    public void A_value_supplied_outside_the_JSON_wins_over_the_empty_declaration()
    {
        // ★ The whole mechanism, in one assertion: the JSON supplies the shape, the secret channel
        // supplies the value, and the later provider wins. User Secrets in development and an
        // environment variable in Azure are both just "a provider added after the JSON" — which is why
        // this is one pattern rather than two different setups.
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(AppSettingsPath(), optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{GroqOptions.SectionName}:{nameof(GroqOptions.ApiKey)}"] = "gsk_supplied_by_the_secret_store",
            })
            .Build();

        var options = new GroqOptions();
        configuration.GetSection(GroqOptions.SectionName).Bind(options);

        options.ApiKey.Should().Be("gsk_supplied_by_the_secret_store");
        // …and the non-secret defaults from the JSON survive the override.
        options.BaseUrl.Should().Be("https://api.groq.com/openai/v1");
    }
}
