using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Wasnie.Application.Common.Abstractions;
using Wasnie.Application.Common.Interfaces;
using Wasnie.Application.Models.Imports;
using Wasnie.Application.Services.Imports;
using Wasnie.Domain.Compensation.Payees;

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

        var emailRequired          = await fieldRequirements.IsRequiredAsync("Payee", "Email",          cancellationToken);
        var hireDateRequired       = await fieldRequirements.IsRequiredAsync("Payee", "HireDate",       cancellationToken);
        var roleRequired           = await fieldRequirements.IsRequiredAsync("Payee", "Role",           cancellationToken);
        var employmentTypeRequired = await fieldRequirements.IsRequiredAsync("Payee", "EmploymentType", cancellationToken);
        var locationRequired       = await fieldRequirements.IsRequiredAsync("Payee", "Location",       cancellationToken);

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
                issues.Add(Error("FullName", "Full name is required.", IssueCategory.Required));
            else if (fullName.Length > 200)
                issues.Add(Error("FullName", "Full name must be 200 characters or fewer.", IssueCategory.Format));

            // ── EmployeeCode ──────────────────────────────────────────────
            var code = GetField(row, mapping.EmployeeCodeColumn);
            if (string.IsNullOrWhiteSpace(code))
                issues.Add(Error("EmployeeCode", "Employee code is required.", IssueCategory.Required));
            else if (code.Length > 50)
                issues.Add(Error("EmployeeCode", "Employee code must be 50 characters or fewer.", IssueCategory.Format));
            else if (existingCodes.Contains(code))
                issues.Add(Error("EmployeeCode", $"Employee code '{code}' already belongs to another payee in this tenant.", IssueCategory.Reference));
            else if (fileCodesInFile.Contains(code))
                issues.Add(Error("EmployeeCode", $"Employee code '{code}' appears more than once in this file.", IssueCategory.Reference));
            else
                fileCodesInFile.Add(code);

            // ── Email ─────────────────────────────────────────────────────
            var email = GetField(row, mapping.EmailColumn);
            if (string.IsNullOrWhiteSpace(email))
            {
                if (emailRequired)
                    issues.Add(Error("Email", "Email is required by your tenant's current settings. Add an email address or change the requirement in Settings.", IssueCategory.Required));
            }
            else if (email.Length > 255)
            {
                issues.Add(Error("Email", "Email must be 255 characters or fewer.", IssueCategory.Format));
            }
            else if (!EmailRegex.IsMatch(email))
            {
                issues.Add(Error("Email", $"'{email}' is not a valid email address.", IssueCategory.Format));
            }
            else if (existingEmails.Contains(email))
            {
                issues.Add(Error("Email", $"Email '{email}' already belongs to another payee in this tenant.", IssueCategory.Reference));
            }
            else if (fileEmailsInFile.Contains(email))
            {
                issues.Add(Error("Email", $"Email '{email}' appears more than once in this file.", IssueCategory.Reference));
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
                    issues.Add(Error("HireDate", "Hire date is required by your tenant's current settings. Add a hire date or change the requirement in Settings.", IssueCategory.Required));
            }
            else if (!TryParseDate(hireDateStr, out var hireDate))
            {
                issues.Add(Error("HireDate", $"'{hireDateStr}' is not a recognisable date. Use YYYY-MM-DD or MM/DD/YYYY.", IssueCategory.Format));
            }
            else
            {
                if (hireDate > today)
                    issues.Add(Error("HireDate", $"Hire date '{hireDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}' is in the future.", IssueCategory.Format));
                else if (hireDate < minDate)
                    issues.Add(Error("HireDate", $"Hire date '{hireDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}' is before the minimum date {minDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.", IssueCategory.Format));
                else if (hireDate >= thirtyDaysAgo)
                    issues.Add(Warn("HireDate", $"Hire date '{hireDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}' is within the last 30 days — verify this is intentional."));
            }

            // ── ManagerEmployeeCode (cross-row) ───────────────────────────
            if (mapping.ManagerEmployeeCodeColumn is not null)
            {
                var managerCode = GetField(row, mapping.ManagerEmployeeCodeColumn);
                if (!string.IsNullOrWhiteSpace(managerCode)
                    && !existingCodes.Contains(managerCode)
                    && !allCodesInFile.Contains(managerCode))
                {
                    issues.Add(Error("ManagerEmployeeCode", $"Manager code '{managerCode}' not found in this tenant or in this file. Create the manager payee first or correct the code.", IssueCategory.Reference));
                }
            }

            // ── Role ──────────────────────────────────────────────────────
            if (mapping.RoleColumn is not null)
            {
                var role = GetField(row, mapping.RoleColumn);
                if (string.IsNullOrWhiteSpace(role))
                {
                    if (roleRequired)
                        issues.Add(Error("Role", "Role is required by your tenant's current settings.", IssueCategory.Required));
                    else
                        issues.Add(Warn("Role", "Role is empty. Consider filling in the payee's role for better reporting."));
                }
            }

            // ── EmploymentType ─────────────────────────────────────────────
            if (mapping.EmploymentTypeColumn is not null)
            {
                var et = GetField(row, mapping.EmploymentTypeColumn);
                if (string.IsNullOrWhiteSpace(et))
                {
                    if (employmentTypeRequired)
                        issues.Add(Error("EmploymentType", "Employment type is required by your tenant's current settings.", IssueCategory.Required));
                }
                else if (!Enum.TryParse<EmploymentType>(et, ignoreCase: true, out _))
                {
                    issues.Add(Error("EmploymentType",
                        $"'{et}' is not a valid employment type. Expected one of: FullTime, PartTime, Temporary, Contractor.", IssueCategory.Format));
                }
            }

            // ── Location ──────────────────────────────────────────────────
            if (mapping.LocationColumn is not null)
            {
                var location = GetField(row, mapping.LocationColumn);
                if (string.IsNullOrWhiteSpace(location))
                {
                    if (locationRequired)
                        issues.Add(Error("Location", "Location is required by your tenant's current settings.", IssueCategory.Required));
                }
                else if (location.Length > 200)
                {
                    issues.Add(Error("Location", "Location must be 200 characters or fewer.", IssueCategory.Format));
                }
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

    private static ValidationIssue Error(string field, string msg, IssueCategory cat = IssueCategory.Other) =>
        new() { Field = field, Message = msg, Severity = IssueSeverity.Error, Category = cat };

    private static ValidationIssue Warn(string field, string msg, IssueCategory cat = IssueCategory.Other) =>
        new() { Field = field, Message = msg, Severity = IssueSeverity.Warning, Category = cat };
}
