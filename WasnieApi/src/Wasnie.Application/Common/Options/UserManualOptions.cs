namespace Wasnie.Application.Common.Options;

/// <summary>
/// Where the user manual PDF is on the server's own disk.
///
/// ★ THE ONE THING THIS DESIGN PROMISES IS AUTHENTICATION, AND ONLY THAT. A PDF a browser can render is
/// a PDF a browser can save; anyone with developer tools has the bytes. So nothing here — and nothing in
/// the UI — claims the manual cannot be downloaded. What it DOES guarantee is that no unauthenticated
/// request ever gets those bytes, because they are served by an endpoint behind the same JWT as the rest
/// of the API and there is no public URL to leak, forward or index.
///
/// ★ WHERE THE FILE ACTUALLY COMES FROM. In development it is the repository's own copy:
/// `Wasnie.Api.csproj` LINKS `docs/Wasnie_User_Manual.pdf` into the output as
/// `Knowledge/Wasnie_User_Manual.pdf` — the same mechanism the assistant's guide already uses, so
/// updating the manual is replacing that one document and rebuilding. Never `bin/` directly: that is
/// generated, and `dotnet clean` deletes it.
///
/// If the manual ever leaves the repository — too large to version, or licensed separately — the literal
/// include has to become a wildcard over a gitignored drop folder, because a literal include of an
/// absent file fails the build for everyone who does not have it.
///
/// ★ WHICH IS WHY THE PATH IS STILL CONFIGURATION. In production the manual generally does NOT live
/// beside the binary: a deployment folder is replaced on release, and a manual inside it would be wiped
/// by a redeploy nobody associated with losing it. There, <see cref="FilePath"/> points at a stable data
/// folder outside the deployment. Same code, two lifecycles.
///
/// ★ SERVING IT FROM CLOUDFLARE LATER IS A SECOND IMPLEMENTATION, NOT A CHANGE HERE. The controller
/// depends on `IUserManualSource`, so a source that fetches from object storage server-side would slot
/// in behind the same endpoint and the browser would not notice. What was deliberately NOT built is a
/// signed public URL handed to the browser: for the few minutes it lives, that URL is a working link
/// outside the application, which is the one property this whole piece exists to avoid.
/// </summary>
public sealed class UserManualOptions
{
    public const string SectionName = "UserManual";

    /// <summary>
    /// The default location: beside the binary, in the same folder the assistant's own documentation
    /// lands in. Deploying the manual is copying one file next to the API — no configuration needed for
    /// the ordinary case.
    /// </summary>
    public const string DefaultRelativePath = "Knowledge/Wasnie_User_Manual.pdf";

    /// <summary>
    /// Absolute path, or a path relative to the application's base directory. Empty means
    /// <see cref="DefaultRelativePath"/>.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// The name the browser sees if it saves the file. Presentation only — it is NOT a barrier and is
    /// not treated as one.
    /// </summary>
    public string FileName { get; init; } = "Wasnie_User_Manual.pdf";
}
