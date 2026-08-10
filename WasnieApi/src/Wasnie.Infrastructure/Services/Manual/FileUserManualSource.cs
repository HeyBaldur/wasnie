using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Manual;

namespace Wasnie.Infrastructure.Services.Manual;

/// <summary>
/// The manual, read from a file on the server and cached in memory once it has been read successfully.
///
/// ★ THE CACHE IS DELIBERATELY ONE-SIDED: a successful read is remembered, a MISS is not. The assistant's
/// knowledge base caches both, and it can — that file is linked from the repository and is always there
/// on a healthy deployment. This one is dropped in by hand, so the interesting moment is exactly the one
/// where it was missing a minute ago and is present now. Caching the miss would mean the manual appears
/// only after an API restart, and whoever copied the file would reasonably conclude the feature is
/// broken.
///
/// ★ AND IT NEVER THROWS. A manual that cannot be read is a 404 on one screen; it is not a reason for a
/// request to fail with a 500, and it must never be a reason the API stops starting.
/// </summary>
public sealed class FileUserManualSource : IUserManualSource
{
    private readonly ILogger<FileUserManualSource> _logger;
    private readonly UserManualOptions _options;
    private readonly object _gate = new();

    private UserManualFile? _cached;

    public FileUserManualSource(IOptions<UserManualOptions> options, ILogger<FileUserManualSource> logger)
    {
        _options = options.Value;
        _logger = logger;
        Location = ResolvePath(_options);
    }

    public string Location { get; }

    public bool IsAvailable => _cached is not null || File.Exists(Location);

    public UserManualFile? Read()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        lock (_gate)
        {
            if (_cached is not null)
            {
                return _cached;
            }

            try
            {
                if (!File.Exists(Location))
                {
                    // Information, not warning: an installation without the manual yet is an expected
                    // state, and the endpoint says so honestly rather than pretending to have it.
                    _logger.LogInformation(
                        "User manual not installed at {Path}; /api/manual/pdf will report it is unavailable.",
                        Location);
                    return null;
                }

                var bytes = File.ReadAllBytes(Location);

                if (bytes.Length == 0)
                {
                    // A zero-byte file is a failed copy, not a manual. Serving it would render as a
                    // blank viewer, which reads to the user as "the manual is empty".
                    _logger.LogWarning("User manual at {Path} is empty; treating it as not installed.", Location);
                    return null;
                }

                _cached = new UserManualFile(bytes, UserManualFile.PdfContentType, _options.FileName);

                _logger.LogInformation(
                    "User manual loaded from {Path} ({Bytes} bytes).", Location, bytes.Length);

                return _cached;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "User manual could not be read from {Path}.", Location);
                return null;
            }
        }
    }

    private static string ResolvePath(UserManualOptions options)
    {
        var configured = string.IsNullOrWhiteSpace(options.FilePath)
            ? UserManualOptions.DefaultRelativePath
            : options.FilePath.Trim();

        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured);
    }
}
