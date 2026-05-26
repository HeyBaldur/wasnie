using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Models.Imports;
using Wasnie.Application.Services.Imports;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/imports")]
[Authorize]
public sealed class ImportsController(
    IFileParserService fileParser,
    IImportCacheService cache,
    IPayeeImportValidationService validator,
    IPayeeImportExecutionService executor,
    ICurrentUserService currentUser) : ControllerBase
{
    private const long MaxFileBytes = 5 * 1024 * 1024; // 5 MB

    // POST /api/imports/payees/parse
    [HttpPost("payees/parse")]
    public async Task<IActionResult> ParsePayees(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file provided." });

        if (file.Length > MaxFileBytes)
            return BadRequest(new { message = "File exceeds the 5 MB limit." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not ".csv" and not ".xlsx")
            return BadRequest(new { message = "Only .csv and .xlsx files are supported." });

        try
        {
            await using var stream = file.OpenReadStream();
            var parsed = await fileParser.ParseAsync(stream, file.FileName, cancellationToken);
            var fileId = cache.Store(parsed, file.FileName);

            return Ok(new ParseResponse
            {
                FileId = fileId,
                Headers = parsed.Headers,
                RowCount = parsed.Rows.Count,
                SampleRows = parsed.Rows.Take(5).ToList(),
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // POST /api/imports/payees/validate
    [HttpPost("payees/validate")]
    public async Task<IActionResult> ValidatePayees(
        [FromBody] ValidateRequest body,
        CancellationToken cancellationToken)
    {
        var cached = cache.Retrieve(body.FileId);
        if (cached is null)
            return BadRequest(new { message = "File session not found or has expired. Please upload the file again." });

        var (parsed, _) = cached.Value;
        var rowResults = await validator.ValidateAsync(parsed.Rows, body.ColumnMapping, cancellationToken);

        return Ok(new ValidateResponse
        {
            TotalRows = parsed.Rows.Count,
            ErrorCount = rowResults.Count(r => r.HasErrors),
            WarningCount = rowResults.Count(r => !r.HasErrors && r.HasWarnings),
            ValidRowCount = rowResults.Count(r => !r.HasErrors),
            RowResults = rowResults,
        });
    }

    // POST /api/imports/payees/execute
    [HttpPost("payees/execute")]
    public async Task<IActionResult> ExecutePayees(
        [FromBody] ExecuteRequest body,
        CancellationToken cancellationToken)
    {
        var cached = cache.Retrieve(body.FileId);
        if (cached is null)
            return BadRequest(new { message = "File session not found or has expired. Please upload the file again." });

        var (parsed, originalFileName) = cached.Value;

        // Re-validate to get fresh results (DB state may have changed since validate call)
        var rowResults = await validator.ValidateAsync(parsed.Rows, body.ColumnMapping, cancellationToken);

        var importedBy = currentUser.Email ?? currentUser.UserId ?? "system";

        var result = await executor.ExecuteAsync(
            parsed.Rows,
            body.ColumnMapping,
            rowResults,
            body.Options.SkipRowsWithWarnings,
            importedBy,
            originalFileName,
            cancellationToken);

        cache.Remove(body.FileId);
        return Ok(result);
    }

    public sealed record ValidateRequest(string FileId, PayeeImportColumnMapping ColumnMapping);
    public sealed record ExecuteRequest(string FileId, PayeeImportColumnMapping ColumnMapping, ExecuteOptions Options);
    public sealed record ExecuteOptions(bool SkipRowsWithWarnings);
}
