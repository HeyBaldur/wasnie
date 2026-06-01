using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Models.Imports;
using Wasnie.Application.Services.Imports;

namespace Wasnie.Infrastructure.Services.Imports;

public sealed class PayeeImportValidationService(
    IApplicationDbContext db,
    IClock clock,
    IFieldRequirementService fieldRequirements) : IPayeeImportValidationService
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(200));

    private static readonly HashSet<string> PersonalEmailDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "hotmail.com", "yahoo.com", "outlook.com",
        "live.com", "icloud.com", "protonmail.com", "aol.com",
    };

    public async Task<List<PayeeRowValidationResult>> ValidateAsync(
        List<Dictionary<string, string>> rows,
        PayeeImportColumnMapping mapping,
        CancellationToken cancellationToken = default)
    {
        var existingCodes = new HashSet<string>(
            await db.Payees.Select(p => p.EmployeeCode).ToListAsync(cancellationToken),
            StringComparer.OrdinalIgnoreCase);

        var existingEmails = new HashSet<string>(
            await db.Payees
                .Where(p => p.Email != null)
                .Select(p => p.Email!)
                .ToListAsync(cancellationToken),
            StringComparer.OrdinalIgnoreCase);

        var emailRequired = await fieldRequirements.IsRequiredAsync("Payee", "Email", cancellationToken);
        var hireDateRequired = await fieldRequirements.IsRequiredAsync("Payee", "HireDate", cancellationToken);

        var fileCodesInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileEmailsInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allCodesInFile = rows
            .Select(r => GetField(r, mapping.EmployeeCodeColumn))
            .Where(c => c.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var thirtyDaysAgo = today.AddDays(-30);
        var minDate = new DateOnly(1950, 1, 1);

        var results = new List<PayeeRowValidationResult>(rows.Count);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNum = i + 1;
            var issues = new List<ValidationIssue>();

            // ── FullName ──────────────────────────────────────────────────
            var fullName = ComposeFullName(row, mapping);
            if (string.IsNullOrWhiteSpace(fullName))
                issues.Add(Error("FullName", "Full name is required."));
            else if (fullName.Length > 200)
                issues.Add(Error("FullName", "Full name must be 200 characters or fewer."));

            // ── EmployeeCode ──────────────────────────────────────────────
            var code = GetField(row, mapping.EmployeeCodeColumn);
            if (string.IsNullOrWhiteSpace(code))
                issues.Add(Error("EmployeeCode", "Employee code is required."));
            else if (code.Length > 50)
                issues.Add(Error("EmployeeCode", "Employee code must be 50 characters or fewer."));
            else if (existingCodes.Contains(code))
                issues.Add(Error("EmployeeCode", $"Employee code '{code}' already exists in the system."));
            else if (fileCodesInFile.Contains(code))
                issues.Add(Error("EmployeeCode", $"Employee code '{code}' appears more than once in this file."));
            else
                fileCodesInFile.Add(code);

            // ── Email ─────────────────────────────────────────────────────
            var email = GetField(row, mapping.EmailColumn);
            if (string.IsNullOrWhiteSpace(email))
            {
                if (emailRequired)
                    issues.Add(Error("Email", "Email is required."));
            }
            else if (email.Length > 255)
            {
                issues.Add(Error("Email", "Email must be 255 characters or fewer."));
            }
            else if (!EmailRegex.IsMatch(email))
            {
                issues.Add(Error("Email", "Email address is not valid."));
            }
            else if (existingEmails.Contains(email))
            {
                issues.Add(Error("Email", $"Email '{email}' already exists in the system."));
            }
            else if (fileEmailsInFile.Contains(email))
            {
                issues.Add(Error("Email", $"Email '{email}' appears more than once in this file."));
            }
            else
            {
                fileEmailsInFile.Add(email);
                var domain = email.Split('@').LastOrDefault() ?? string.Empty;
                if (PersonalEmailDomains.Contains(domain))
                    issues.Add(Warn("Email", $"'{domain}' looks like a personal email domain. Verify this is intentional."));
            }

            // ── HireDate ──────────────────────────────────────────────────
            var hireDateStr = GetField(row, mapping.HireDateColumn);
            if (string.IsNullOrWhiteSpace(hireDateStr))
            {
                if (hireDateRequired)
                    issues.Add(Error("HireDate", "Hire date is required."));
            }
            else if (!TryParseDate(hireDateStr, out var hireDate))
            {
                issues.Add(Error("HireDate", $"'{hireDateStr}' is not a recognisable date. Use YYYY-MM-DD or MM/DD/YYYY."));
            }
            else
            {
                if (hireDate > today)
                    issues.Add(Error("HireDate", "Hire date cannot be in the future."));
                else if (hireDate < minDate)
                    issues.Add(Error("HireDate", $"Hire date cannot be before {minDate:yyyy-MM-dd}."));
                else if (hireDate >= thirtyDaysAgo)
                    issues.Add(Warn("HireDate", "Hire date is within the last 30 days — verify this is intentional."));
            }

            // ── ManagerEmployeeCode (cross-row) ───────────────────────────
            if (mapping.ManagerEmployeeCodeColumn is not null)
            {
                var managerCode = GetField(row, mapping.ManagerEmployeeCodeColumn);
                if (!string.IsNullOrWhiteSpace(managerCode)
                    && !existingCodes.Contains(managerCode)
                    && !allCodesInFile.Contains(managerCode))
                {
                    issues.Add(Error("ManagerEmployeeCode", $"Manager code '{managerCode}' does not match any existing payee or any other row in this file."));
                }
            }

            // ── Role (optional, warn if empty) ────────────────────────────
            if (mapping.RoleColumn is not null)
            {
                var role = GetField(row, mapping.RoleColumn);
                if (string.IsNullOrWhiteSpace(role))
                    issues.Add(Warn("Role", "Role is empty. Consider filling in the payee's role for better reporting."));
            }

            results.Add(new PayeeRowValidationResult
            {
                RowNumber = rowNum,
                OriginalData = row,
                Issues = issues,
            });
        }

        return results;
    }

    private static string ComposeFullName(Dictionary<string, string> row, PayeeImportColumnMapping mapping)
    {
        if (mapping.FullNameColumns.Length > 0)
            return string.Join(' ', mapping.FullNameColumns.Select(c => GetField(row, c))).Trim();
        return GetField(row, mapping.FullNameColumn);
    }

    private static string GetField(Dictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var val) ? val.Trim() : string.Empty;

    private static bool TryParseDate(string s, out DateOnly result)
    {
        string[] formats = ["yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy", "M/d/yyyy", "d/M/yyyy", "yyyy/MM/dd"];
        foreach (var fmt in formats)
        {
            if (DateOnly.TryParseExact(s, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
                return true;
        }
        result = default;
        return false;
    }

    private static ValidationIssue Error(string field, string msg) =>
        new() { Field = field, Message = msg, Severity = IssueSeverity.Error };

    private static ValidationIssue Warn(string field, string msg) =>
        new() { Field = field, Message = msg, Severity = IssueSeverity.Warning };
}
