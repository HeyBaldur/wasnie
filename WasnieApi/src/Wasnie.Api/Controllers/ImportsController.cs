using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Common.Options;
using Wasnie.Application.Models.Imports;
using Wasnie.Application.Services.Imports;
using Wasnie.Domain.Authorization;
using IWasnieAuthorizationService = Wasnie.Application.Common.Interfaces.IAuthorizationService;

namespace Wasnie.Api.Controllers;

[ApiController]
[Route("api/imports")]
[Authorize]
public sealed class ImportsController(
    IFileParserService fileParser,
    IImportCacheService cache,
    IPayeeImportValidationService validator,
    IPayeeImportExecutionService executor,
    ICurrentUserService currentUser,
    ITenantContext tenantContext,
    ITransactionImportValidationService transactionValidator,
    ITransactionUpdateValidationService updateValidator,
    IBackgroundJobService backgroundJobService,
    IWasnieAuthorizationService authorizationService,
    IOptions<ImportOptions> importOptions) : ControllerBase
{
    private const long MaxFileBytes = 5 * 1024 * 1024; // 5 MB
    private ImportOptions Imports => importOptions.Value;

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
            var parsed = await fileParser.ParseAsync(stream, file.FileName, Imports.PayeeMaxRows, cancellationToken);
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

        if (result.Blocked)
        {
            return Conflict(new
            {
                blocked = true,
                reason = "payee_limit",
                current = result.BlockedCurrent,
                incoming = result.BlockedIncoming,
                limit = result.BlockedLimit,
                tier = result.BlockedTier,
            });
        }

        return Ok(result);
    }

    // POST /api/imports/transactions/parse
    [HttpPost("transactions/parse")]
    public async Task<IActionResult> ParseTransactions(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsCreate, cancellationToken);

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
            var parsed = await fileParser.ParseAsync(stream, file.FileName, Imports.TransactionMaxRows, cancellationToken);
            var fileId = cache.Store(parsed, file.FileName, "transactions");

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

    // POST /api/imports/transactions/validate
    [HttpPost("transactions/validate")]
    public async Task<IActionResult> ValidateTransactions(
        [FromBody] TransactionValidateRequest body,
        CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsCreate, cancellationToken);

        var cached = cache.Retrieve(body.FileId, "transactions");
        if (cached is null)
            return BadRequest(new { message = "File session not found or has expired. Please upload the file again." });

        var (parsed, _) = cached.Value;
        var rowResults = await transactionValidator.ValidateAsync(parsed.Rows, body.ColumnMapping, cancellationToken);

        return Ok(new TransactionValidateResponse
        {
            TotalRows = parsed.Rows.Count,
            ErrorCount = rowResults.Count(r => r.HasErrors),
            WarningCount = rowResults.Count(r => !r.HasErrors && r.HasWarnings),
            ValidRowCount = rowResults.Count(r => !r.HasErrors),
            RowResults = rowResults,
        });
    }

    // POST /api/imports/transactions/execute
    [HttpPost("transactions/execute")]
    public async Task<IActionResult> ExecuteTransactions(
        [FromBody] TransactionExecuteRequest body,
        CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsCreate, cancellationToken);

        var cached = cache.Retrieve(body.FileId, "transactions");
        if (cached is null)
            return BadRequest(new { message = "File session not found or has expired. Please upload the file again." });

        var (parsed, originalFileName) = cached.Value;

        var txPayload = new TransactionImportPayload(
            TenantId: tenantContext.TenantId,
            RequestedByUserId: currentUser.UserId ?? "system",
            RequestedByEmail: currentUser.Email ?? string.Empty,
            OriginalFileName: originalFileName,
            ColumnMapping: body.ColumnMapping,
            Options: new TransactionImportOptions(body.Options.SkipRowsWithWarnings),
            Rows: parsed.Rows);

        var jobId = await backgroundJobService.EnqueueAsync(
            txPayload,
            tenantContext.TenantId,
            currentUser.UserId ?? "system",
            currentUser.Email ?? string.Empty,
            cancellationToken);

        cache.Remove(body.FileId, "transactions");

        return StatusCode(202, new TransactionExecuteAccepted(jobId));
    }

    // GET /api/imports/transactions/limits
    [HttpGet("transactions/limits")]
    public IActionResult GetTransactionLimits()
    {
        return Ok(new { maxRows = Imports.TransactionMaxRows });
    }

    // ── Update-from-Excel endpoints ────────────────────────────────────────────

    // POST /api/imports/transactions/update/parse  (reuses same parse logic)
    [HttpPost("transactions/update/parse")]
    public async Task<IActionResult> ParseTransactionsUpdate(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsUpdateFromExcel, cancellationToken);

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
            var parsed = await fileParser.ParseAsync(stream, file.FileName, Imports.TransactionMaxRows, cancellationToken);
            var fileId = cache.Store(parsed, file.FileName, "transaction-update");

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

    // POST /api/imports/transactions/update/validate
    [HttpPost("transactions/update/validate")]
    public async Task<IActionResult> ValidateTransactionsUpdate(
        [FromBody] TransactionUpdateValidateRequest body,
        CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsUpdateFromExcel, cancellationToken);

        var cached = cache.Retrieve(body.FileId, "transaction-update");
        if (cached is null)
            return BadRequest(new { message = "File session not found or has expired. Please upload the file again." });

        var (parsed, _) = cached.Value;
        var rowResults = await updateValidator.ValidateAsync(parsed.Rows, body.ColumnMapping, cancellationToken);

        return Ok(new TransactionUpdateValidateResponse
        {
            TotalRows = parsed.Rows.Count,
            WillUpdateCount = rowResults.Count(r => r.Status == UpdateRowStatus.WillUpdate),
            NoChangesCount = rowResults.Count(r => r.Status == UpdateRowStatus.NoChanges),
            ErrorCount = rowResults.Count(r => r.Status == UpdateRowStatus.Error),
            RowResults = rowResults,
        });
    }

    // POST /api/imports/transactions/update/execute
    [HttpPost("transactions/update/execute")]
    public async Task<IActionResult> ExecuteTransactionsUpdate(
        [FromBody] TransactionUpdateExecuteRequest body,
        CancellationToken cancellationToken)
    {
        await authorizationService.RequireAsync(Permission.TransactionsUpdateFromExcel, cancellationToken);

        var cached = cache.Retrieve(body.FileId, "transaction-update");
        if (cached is null)
            return BadRequest(new { message = "File session not found or has expired. Please upload the file again." });

        var (parsed, originalFileName) = cached.Value;

        var updatePayload = new TransactionUpdatePayload(
            TenantId: tenantContext.TenantId,
            RequestedByUserId: currentUser.UserId ?? "system",
            RequestedByEmail: currentUser.Email ?? string.Empty,
            OriginalFileName: originalFileName,
            ColumnMapping: body.ColumnMapping,
            Rows: parsed.Rows);

        var jobId = await backgroundJobService.EnqueueAsync(
            updatePayload,
            tenantContext.TenantId,
            currentUser.UserId ?? "system",
            currentUser.Email ?? string.Empty,
            cancellationToken);

        cache.Remove(body.FileId, "transaction-update");

        return StatusCode(202, new TransactionUpdateExecuteAccepted(jobId));
    }

    public sealed record ValidateRequest(string FileId, PayeeImportColumnMapping ColumnMapping);
    public sealed record ExecuteRequest(string FileId, PayeeImportColumnMapping ColumnMapping, ExecuteOptions Options);
    public sealed record ExecuteOptions(bool SkipRowsWithWarnings);

    public sealed record TransactionValidateRequest(string FileId, TransactionImportColumnMapping ColumnMapping);
    public sealed record TransactionExecuteRequest(string FileId, TransactionImportColumnMapping ColumnMapping, TransactionExecuteOptions Options);
    public sealed record TransactionExecuteOptions(bool SkipRowsWithWarnings);

    public sealed record TransactionUpdateValidateRequest(string FileId, TransactionUpdateColumnMapping ColumnMapping);
    public sealed record TransactionUpdateExecuteRequest(string FileId, TransactionUpdateColumnMapping ColumnMapping);
}
