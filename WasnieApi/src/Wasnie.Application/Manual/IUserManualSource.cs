namespace Wasnie.Application.Manual;

/// <summary>The manual's bytes and how to label them. Null is "there is no manual to serve".</summary>
public sealed record UserManualFile(byte[] Bytes, string ContentType, string FileName)
{
    public const string PdfContentType = "application/pdf";
}

/// <summary>
/// Where the user manual comes from, kept behind an interface so the answer can change without the
/// endpoint changing.
///
/// ★ THE ENDPOINT IS THE PRODUCT DECISION; THE SOURCE IS AN IMPLEMENTATION DETAIL. Today the manual is a
/// file on the server's disk. If it later lives in Cloudflare or S3, a source that fetches it
/// server-side implements this same interface and the browser still talks to one authenticated Wasnie
/// endpoint. That is the property worth protecting: the client never learns a URL that works without a
/// Wasnie session.
/// </summary>
public interface IUserManualSource
{
    /// <summary>
    /// Cheap enough to call on a request: it does not read the file, it only reports whether there is
    /// one. Lets the caller answer "no manual installed" without loading megabytes to find out.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>The resolved location, for logs and for telling an operator where to put the file.</summary>
    string Location { get; }

    /// <summary>The manual, or null when it is not installed or could not be read.</summary>
    UserManualFile? Read();
}
