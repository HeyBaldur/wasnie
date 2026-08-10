using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wasnie.Application.Assistant.Common;
using Wasnie.Application.Common.Options;
using Wasnie.Infrastructure.Services.Assistant;
using Wasnie.Infrastructure.Services.Manual;

namespace Wasnie.UnitTests.Application;

/// <summary>
/// The user manual's source.
///
/// ★ WHAT THESE PROVE. Not that a PDF is unreachable — it is not, and no test could claim otherwise
/// honestly. They pin the two things that would break the feature quietly: a missing manual degrades to
/// "not installed" instead of throwing on a user's request, and a manual that appears AFTER start-up is
/// picked up without a restart.
/// </summary>
public sealed class UserManualSourceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"wasnie-manual-{Guid.NewGuid():N}");

    public UserManualSourceTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }

    private FileUserManualSource Source(string fileName = "manual.pdf") =>
        new(
            Options.Create(new UserManualOptions { FilePath = Path.Combine(_directory, fileName) }),
            NullLogger<FileUserManualSource>.Instance);

    [Fact]
    public void A_missing_manual_is_reported_as_unavailable_rather_than_throwing()
    {
        var source = Source();

        source.IsAvailable.Should().BeFalse();
        source.Read().Should().BeNull("a deployment without the manual is an expected state, not a fault");
    }

    [Fact]
    public void An_installed_manual_is_read_with_the_PDF_content_type()
    {
        var path = Path.Combine(_directory, "manual.pdf");
        File.WriteAllBytes(path, "%PDF-1.7 pretend"u8.ToArray());

        var source = Source();

        source.IsAvailable.Should().BeTrue();

        var file = source.Read();
        file.Should().NotBeNull();
        file!.ContentType.Should().Be("application/pdf");
        file.Bytes.Should().NotBeEmpty();
    }

    [Fact]
    public void A_manual_copied_in_AFTER_start_up_is_picked_up_without_a_restart()
    {
        // ★ THE ONE-SIDED CACHE, ASSERTED. A hit is cached; a MISS is not. Remembering the miss would
        // mean the operator copies the PDF onto the server, reloads the page, still sees "not available",
        // and reasonably concludes the feature is broken.
        var source = Source();
        source.Read().Should().BeNull();

        File.WriteAllBytes(Path.Combine(_directory, "manual.pdf"), "%PDF-1.7 pretend"u8.ToArray());

        source.IsAvailable.Should().BeTrue();
        source.Read().Should().NotBeNull();
    }

    [Fact]
    public void A_zero_byte_file_is_treated_as_not_installed()
    {
        // A failed copy renders as a blank viewer, which reads to the user as "the manual is empty".
        File.WriteAllBytes(Path.Combine(_directory, "manual.pdf"), []);

        Source().Read().Should().BeNull();
    }

    [Fact]
    public void A_relative_path_resolves_beside_the_binary_and_the_default_is_the_knowledge_folder()
    {
        var source = new FileUserManualSource(
            Options.Create(new UserManualOptions()), NullLogger<FileUserManualSource>.Instance);

        source.Location.Should().StartWith(AppContext.BaseDirectory);
        source.Location.Should().EndWith("Wasnie_User_Manual.pdf");
    }

    // ── The screen and the assistant read ONE document ────────────────────────

    [Fact]
    public void The_REAL_guide_contains_every_section_the_screen_offers_as_a_shortcut()
    {
        // ★ THE MANUAL SCREEN AND THE ASSISTANT NOW READ ONE DOCUMENT — `/api/manual/content` serves this
        // very knowledge base. The drift that closes was real and silent: the assistant answered from the
        // markdown while the screen showed an exported PDF, and nothing regenerated one from the other, so
        // editing the guide made the two quietly disagree.
        //
        // ★ AND THIS IS WHAT GUARDS THE SHORTCUTS. The panel matches these section numbers against the
        // headings actually in the document and silently drops any that is missing — correct behaviour,
        // but it means a renamed heading would make a shortcut vanish with nobody noticing. This is the
        // test that notices.
        var knowledge = new FileAssistantKnowledgeBase(NullLogger<FileAssistantKnowledgeBase>.Instance);
        knowledge.IsAvailable.Should().BeTrue("the guide ships next to the binary — see Wasnie.Infrastructure.csproj");

        foreach (var heading in new[]
                 {
                     "## 5. Rate tables",
                     "### 4.4 Modifier, Cap, Floor",
                     "## 6. SplitAtQuota",
                     "### 4.5 Plan lifecycle",
                 })
        {
            knowledge.Documentation.Should().Contain(
                heading,
                $"the manual screen offers '{heading}' as a shortcut; renaming it drops the shortcut silently");
        }
    }

    // ── The assistant's end of the same feature ───────────────────────────────

    [Fact]
    public void The_assistant_links_the_manual_by_its_INTERNAL_route_and_never_to_a_file()
    {
        // ★ THE WHOLE POINT OF THE ROUTE. Sending the user to the PDF's own address would mean there IS
        // a public address — the one property this design refuses. The assistant links the screen, the
        // screen fetches the bytes with the session. If this ever becomes an https:// link to a file,
        // the barrier is gone and this test is what says so.
        AssistantPrompt.ManualGuidance.Should().Contain("[User manual](/manual)");
        AssistantPrompt.ManualGuidance.Should().NotContain("http");

        // And it reaches the two prompts that use it — including the no-source prompt, which ships
        // WITHOUT the navigation map and so cannot rely on rule 6 recognising the route.
        AssistantPrompt.IgnoranceRules.Should().Contain(AssistantPrompt.ManualGuidance);
        AssistantPrompt.NoSourcePrompt.Should().Contain(AssistantPrompt.ManualGuidance);
    }
}
