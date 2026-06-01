namespace Wasnie.Application.Models.Imports;

public enum IssueSeverity { Error, Warning }

public enum IssueCategory
{
    Reference,  // identifier not found, duplicate, or conflict
    Format,     // value is the wrong shape (bad date, bad number, etc.)
    Required,   // field required by current tenant settings
    Other,      // default / unclassified
}

public sealed class ValidationIssue
{
    public required string Field { get; init; }
    public required string Message { get; init; }
    public required IssueSeverity Severity { get; init; }
    public IssueCategory Category { get; init; } = IssueCategory.Other;
}

public sealed class PayeeRowValidationResult
{
    public required int RowNumber { get; init; }
    public required Dictionary<string, string> OriginalData { get; init; }
    public required List<ValidationIssue> Issues { get; init; }
    public bool HasErrors => Issues.Exists(i => i.Severity == IssueSeverity.Error);
    public bool HasWarnings => Issues.Exists(i => i.Severity == IssueSeverity.Warning);
}

public sealed class PayeeImportResult
{
    public required int TotalRows { get; init; }
    public required int CreatedCount { get; init; }
    public required int SkippedCount { get; init; }
    public required List<PayeeRowValidationResult> SkippedRows { get; init; }
}

public sealed class ParseResponse
{
    public required string FileId { get; init; }
    public required string[] Headers { get; init; }
    public required int RowCount { get; init; }
    public required List<Dictionary<string, string>> SampleRows { get; init; }
}

public sealed class ValidateResponse
{
    public required int TotalRows { get; init; }
    public required int ErrorCount { get; init; }
    public required int WarningCount { get; init; }
    public required int ValidRowCount { get; init; }
    public required List<PayeeRowValidationResult> RowResults { get; init; }
}

public sealed class TransactionRowValidationResult
{
    public required int RowNumber { get; init; }
    public required Dictionary<string, string> OriginalData { get; init; }
    public required List<ValidationIssue> Issues { get; init; }
    public bool HasErrors => Issues.Exists(i => i.Severity == IssueSeverity.Error);
    public bool HasWarnings => Issues.Exists(i => i.Severity == IssueSeverity.Warning);
}

public sealed class TransactionValidateResponse
{
    public required int TotalRows { get; init; }
    public required int ErrorCount { get; init; }
    public required int WarningCount { get; init; }
    public required int ValidRowCount { get; init; }
    public required List<TransactionRowValidationResult> RowResults { get; init; }
}

public sealed record TransactionExecuteAccepted(Guid JobId);
