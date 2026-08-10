using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Assistant.Abstractions;
using Wasnie.Application.Manual;

namespace Wasnie.Api.Controllers;

/// <summary>
/// The user manual, served to signed-in users and to nobody else.
///
/// ★ [Authorize] IS THE WHOLE SECURITY MODEL, AND IT IS STATED PLAINLY BECAUSE THE ALTERNATIVE WOULD BE
/// A LIE. A PDF a browser renders is a PDF a browser can save — no viewer, no header and no disabled
/// button changes that, and the code does not pretend otherwise. What this class does guarantee is the
/// property that actually holds: the bytes exist at exactly one address, that address requires a valid
/// Wasnie token, and there is no public URL anywhere that could be forwarded, indexed or leaked. Anyone
/// who has the file had a session first.
///
/// ★ NO RBAC PERMISSION ON PURPOSE. The manual documents the product; it contains no tenant data, no
/// payee, no amount. Gating it behind a permission would hide the instructions from exactly the people
/// with the fewest rights, who are the ones most likely to need them. Authentication is the line.
///
/// ★ NOT A ROLE FOR THE ASSISTANT'S KNOWLEDGE BASE. That corpus is text the model reads; this is a
/// document a person reads. Same subject, two different artefacts with different lifecycles — see
/// IUserManualSource.
/// </summary>
[ApiController]
[Route("api/manual")]
[Authorize]
public sealed class ManualController(
    IUserManualSource manual,
    IAssistantKnowledgeBase knowledge) : ControllerBase
{
    /// <summary>
    /// GET /api/manual/content — the manual as the MARKDOWN it is authored in.
    ///
    /// ★ THE SAME OBJECT THE ASSISTANT READS, AND THAT IS THE ENTIRE POINT. `IAssistantKnowledgeBase`
    /// already loads `docs/Wasnie_Configuration_Guide.md`, which is the file the team edits and publishes
    /// the handbook from. Serving the screen from that same instance means the manual a user reads and
    /// the answers the assistant gives cannot drift apart — they are one document.
    ///
    /// ★ WHICH FIXES A DRIFT THAT ALREADY EXISTED, SILENTLY. Until now the assistant answered from the
    /// markdown while the screen showed an exported PDF, and nothing regenerated one from the other: edit
    /// the guide, and the two would quietly start contradicting each other with no build step to notice.
    /// The PDF stays available as the printable export; the markdown is the source of truth.
    ///
    /// Markdown, not HTML: rendering belongs to the client, which already has `marked` and a sanitiser,
    /// and shipping HTML from the server would make this endpoint responsible for presentation.
    /// </summary>
    [HttpGet("content")]
    public IActionResult Content()
    {
        if (!knowledge.IsAvailable)
        {
            return NotFound(new { message = "The user manual is not available on this installation." });
        }

        return Ok(new { markdown = knowledge.Documentation });
    }

    /// <summary>
    /// GET /api/manual/status — whether a manual is installed, without shipping it.
    ///
    /// Exists so the screen can show an honest empty state without downloading megabytes to discover
    /// there is nothing to show, and so an operator can check a deployment with one cheap call.
    /// </summary>
    [HttpGet("status")]
    public IActionResult Status() => Ok(new { available = manual.IsAvailable });

    /// <summary>
    /// GET /api/manual/pdf — the bytes, for a caller that proved it has a session.
    ///
    /// ★ INLINE, NOT ATTACHMENT: no file name is passed to <c>File</c>, so ASP.NET emits no
    /// Content-Disposition and the browser renders the document instead of saving it. That is a
    /// PRESENTATION choice — the viewer needs to display it — and not a download barrier. Naming it here
    /// would make every open a download prompt, which is the opposite of what the screen is for.
    /// </summary>
    [HttpGet("pdf")]
    public IActionResult Pdf()
    {
        var file = manual.Read();

        if (file is null)
        {
            // 404 with a reason an operator can act on. It says the manual is not installed — it does
            // NOT say where on disk it was looked for: that is server topology, and this endpoint
            // answers to any signed-in user of any tenant.
            return NotFound(new { message = "The user manual is not available on this installation." });
        }

        return File(file.Bytes, file.ContentType);
    }
}
