using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Models.Imports;
using Wasnie.Application.Services.Imports;
using Wasnie.Domain.Compensation.Payees;
using Wasnie.Domain.Entities;

namespace Wasnie.Infrastructure.Services.Imports;

public sealed class PayeeImportExecutionService(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ILogger<PayeeImportExecutionService> logger,
    IClock clock,
    IGuidGenerator guid,
    ITierLimitChecker tierLimitChecker) : IPayeeImportExecutionService
{
    public async Task<PayeeImportResult> ExecuteAsync(
        List<Dictionary<string, string>> rows,
        PayeeImportColumnMapping mapping,
        List<PayeeRowValidationResult> validationResults,
        bool skipRowsWithWarnings,
        string importedBy,
        string originalFileName,
        CancellationToken cancellationToken = default)
    {
        var startedAt = clock.UtcNowOffset;
        var today = DateOnly.FromDateTime(clock.UtcNow);

        var skipped = new List<PayeeRowValidationResult>();
        var toImport = new List<(int Index, Dictionary<string, string> Row)>();

        for (var i = 0; i < rows.Count; i++)
        {
            var vr = validationResults[i];
            if (vr.HasErrors || (skipRowsWithWarnings && vr.HasWarnings))
                skipped.Add(vr);
            else
                toImport.Add((i, rows[i]));
        }

        // Tier limit pre-flight: reject the entire import if it would exceed the tenant's payee cap.
        var limitCheck = await tierLimitChecker.CheckPayeeImportLimitAsync(toImport.Count, cancellationToken);
        if (limitCheck.Blocked)
        {
            logger.LogWarning(
                "Payee import blocked by tier limit: tenant has {Current}/{Limit} payees, file contains {Incoming} valid rows (tier: {Tier})",
                limitCheck.Current, limitCheck.Limit, toImport.Count, limitCheck.Tier);

            return new PayeeImportResult
            {
                TotalRows = rows.Count,
                CreatedCount = 0,
                SkippedCount = 0,
                SkippedRows = [],
                Blocked = true,
                BlockedCurrent = limitCheck.Current,
                BlockedIncoming = toImport.Count,
                BlockedLimit = limitCheck.Limit,
                BlockedTier = limitCheck.Tier,
            };
        }

        // Pre-load existing employee codes to resolve manager references
        var existingPayeesByCode = await db.Payees
            .ToDictionaryAsync(p => p.EmployeeCode, p => p.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        // Phase 1: create all payees without manager assignment
        var newPayees = new Dictionary<string, Payee>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, row) in toImport)
        {
            var fullName = ComposeFullName(row, mapping);
            var code = GetField(row, mapping.EmployeeCodeColumn);
            var email = GetField(row, mapping.EmailColumn);
            var hireDateStr = GetField(row, mapping.HireDateColumn);
            var role = mapping.RoleColumn is not null ? GetField(row, mapping.RoleColumn) : null;
            var etStr = mapping.EmploymentTypeColumn is not null ? GetField(row, mapping.EmploymentTypeColumn) : null;
            var location = mapping.LocationColumn is not null ? GetField(row, mapping.LocationColumn) : null;

            // Honour the parse result: an unmapped column or a blank cell must persist as NULL,
            // not default(DateOnly) (0001-01-01). Unparseable values are already a blocking error
            // in PayeeImportValidationService, so reaching null here means "genuinely absent".
            DateOnly? hireDate = TryParseDate(hireDateStr, out var parsedHireDate) ? parsedHireDate : null;

            EmploymentType? employmentType = null;
            if (!string.IsNullOrWhiteSpace(etStr) &&
                Enum.TryParse<EmploymentType>(etStr, ignoreCase: true, out var parsedEt))
                employmentType = parsedEt;

            var payee = Payee.Create(
                tenantContext.TenantId,
                fullName,
                code,
                email,
                hireDate,
                importedBy,
                guid.NewGuid(),
                startedAt,
                string.IsNullOrWhiteSpace(role) ? null : role,
                managerId: null,
                employmentType,
                string.IsNullOrWhiteSpace(location) ? null : location);

            db.Payees.Add(payee);
            newPayees[code] = payee;
        }

        await db.SaveChangesAsync(cancellationToken);

        // Phase 2: resolve manager cross-references
        if (mapping.ManagerEmployeeCodeColumn is not null)
        {
            var hasManagerUpdates = false;
            foreach (var (_, row) in toImport)
            {
                var code = GetField(row, mapping.EmployeeCodeColumn);
                var managerCode = GetField(row, mapping.ManagerEmployeeCodeColumn);
                if (string.IsNullOrWhiteSpace(managerCode))
                    continue;

                Guid? managerId = null;
                if (newPayees.TryGetValue(managerCode, out var inFileManager))
                    managerId = inFileManager.Id;
                else if (existingPayeesByCode.TryGetValue(managerCode, out var existingId))
                    managerId = existingId;

                if (managerId.HasValue && newPayees.TryGetValue(code, out var payee))
                {
                    payee.Update(payee.FullName, payee.EmployeeCode, payee.Email, payee.HireDate, payee.Role, managerId, importedBy, clock.UtcNowOffset);
                    hasManagerUpdates = true;
                }
            }

            if (hasManagerUpdates)
                await db.SaveChangesAsync(cancellationToken);
        }

        var result = new PayeeImportResult
        {
            TotalRows = rows.Count,
            CreatedCount = toImport.Count,
            SkippedCount = skipped.Count,
            SkippedRows = skipped,
        };

        // Audit record — failure is non-fatal: payees are already committed.
        try
        {
            var completedAt = clock.UtcNowOffset;
            var auditStatus = skipped.Count == 0 ? "Success"
                : toImport.Count == 0 ? "Failed"
                : "PartialSuccess";

            var audit = ImportAudit.Create(
                guid.NewGuid(),
                tenantContext.TenantId,
                importedBy,
                startedAt,
                completedAt,
                "Payees",
                rows.Count,
                toImport.Count,
                skipped.Count,
                originalFileName,
                auditStatus);

            db.ImportAudits.Add(audit);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to write ImportAudit record for file '{FileName}' ({Created} created, {Skipped} skipped). " +
                "Payees were already saved — this is an audit-only failure.",
                originalFileName, toImport.Count, skipped.Count);
        }

        return result;
    }

    private static string ComposeFullName(Dictionary<string, string> row, PayeeImportColumnMapping mapping)
    {
        if (mapping.FullNameColumns.Length > 0)
            return string.Join(' ', mapping.FullNameColumns.Select(c => GetField(row, c))).Trim();
        return GetField(row, mapping.FullNameColumn);
    }

    private static string GetField(Dictionary<string, string> row, string? column) =>
        column is not null && row.TryGetValue(column, out var val) ? val.Trim() : string.Empty;

    private static bool TryParseDate(string s, out DateOnly result)
    {
        string[] formats = ["yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy", "M/d/yyyy", "d/M/yyyy", "yyyy/MM/dd"];
        foreach (var fmt in formats)
        {
            if (DateOnly.TryParseExact(s, fmt, null, System.Globalization.DateTimeStyles.None, out result))
                return true;
        }
        result = default;
        return false;
    }
}
