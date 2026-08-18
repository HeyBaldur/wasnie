using Wasnie.Domain.Common;
using Wasnie.Domain.Exceptions;

namespace Wasnie.Domain.Compensation.Payees;

public sealed class Payee : BaseAuditableEntity
{
    public string FullName { get; private set; } = string.Empty;
    public string EmployeeCode { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Role { get; private set; }

    /// <summary>
    /// The identity-system user who OWNS this payee — the person whose money this is.
    ///
    /// Null means "not linked yet", and that is a deliberate, permanent possibility rather than a
    /// migration artefact: a payee is a compensation record, not an account. Contractors paid through
    /// the system who never log in, and people imported before they are provisioned, legitimately have
    /// no user.
    ///
    /// ★ NULL IS THE CLOSED STATE, NOT THE OPEN ONE. Authorisation reads this field to answer "is this
    /// payee yours" (see PayeeAccessGuard). An unlinked payee therefore belongs to NOBODY: no Rep and
    /// no Manager can reach it, only the supervisory roles. The opposite default — unlinked means
    /// visible — is the shape of the BOLA hole this field exists to close.
    ///
    /// A string because ASP.NET Identity's key is a string (<c>IdentityUser</c>), and it is compared
    /// against <c>ClaimTypes.NameIdentifier</c> straight off the token. Storing it as a Guid would mean
    /// parsing the claim, and a parse failure on an authorisation path fails OPEN far too easily.
    /// </summary>
    public string? UserId { get; private set; }

    public Guid? ManagerId { get; private set; }
    public DateOnly? HireDate { get; private set; }
    public DateOnly? TerminationDate { get; private set; }
    public PayeeStatus Status { get; private set; } = PayeeStatus.Active;
    public EmploymentType? EmploymentType { get; private set; }
    public string? Location { get; private set; }

    // Platform assignment eligibility (Decision G). Orthogonal to PayeeStatus (HR status).
    // When false, new transactions cannot be assigned to this payee.
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset? DeactivatedAt { get; private set; }

    private Payee() { }

    public static Payee Create(
        Guid tenantId,
        string fullName,
        string employeeCode,
        string? email,
        DateOnly? hireDate,
        string createdBy,
        Guid id,
        DateTimeOffset now,
        string? role = null,
        Guid? managerId = null,
        EmploymentType? employmentType = null,
        string? location = null)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException("Full name is required.");
        if (string.IsNullOrWhiteSpace(employeeCode))
            throw new DomainException("Employee code is required.");
        if (hireDate.HasValue && hireDate.Value > DateOnly.FromDateTime(now.UtcDateTime))
            throw new DomainException("Hire date cannot be in the future.");

        return new Payee
        {
            Id = id,
            TenantId = tenantId,
            FullName = fullName.Trim(),
            EmployeeCode = employeeCode.Trim(),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant(),
            Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim(),
            ManagerId = managerId,
            HireDate = hireDate,
            EmploymentType = employmentType,
            Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
            Status = PayeeStatus.Active,
            IsActive = true,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedBy = createdBy
        };
    }

    public void Update(
        string fullName,
        string employeeCode,
        string? email,
        DateOnly? hireDate,
        string? role,
        Guid? managerId,
        string updatedBy,
        DateTimeOffset now,
        EmploymentType? employmentType = null,
        string? location = null)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException("Full name is required.");
        if (string.IsNullOrWhiteSpace(employeeCode))
            throw new DomainException("Employee code is required.");
        if (managerId == Id)
            throw new DomainException("A payee cannot be their own manager.");

        FullName = fullName.Trim();
        EmployeeCode = employeeCode.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim();
        ManagerId = managerId;
        HireDate = hireDate;
        EmploymentType = employmentType;
        Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    /// <summary>
    /// Assigns (or clears) the manager without touching any other field.
    /// </summary>
    /// <remarks>
    /// Update() is a full replace: every field it declares is assigned unconditionally, and the
    /// optional parameters default to null. A caller that only wants to change the manager but goes
    /// through Update() must therefore re-supply every other field, and silently erases any it
    /// forgets. The payee import's manager-resolution pass did exactly that and wiped EmploymentType
    /// and Location off every payee that had a manager code.
    ///
    /// This method exists so that a partial update cannot lose data by omission: fields added to
    /// Payee in the future are unaffected here by construction.
    /// </remarks>
    public void AssignManager(Guid? managerId, string updatedBy, DateTimeOffset now)
    {
        if (managerId == Id)
            throw new DomainException("A payee cannot be their own manager.");

        ManagerId = managerId;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    /// <summary>
    /// Links this payee to the identity-system user who owns it.
    ///
    /// Its own method, and deliberately NOT a parameter of <see cref="Update"/>: Update is a full
    /// replace whose optional parameters default to null, so a caller that forgot to re-supply the
    /// user id would silently UNLINK the payee — and an unlink is a change of who owns money. Ownership
    /// moves only when somebody asks for it to move.
    /// </summary>
    public void LinkToUser(string userId, string updatedBy, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new DomainException("A user id is required to link a payee to a user.");

        UserId = userId.Trim();
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    /// <summary>
    /// Breaks the link. The payee stops being reachable by any Rep or Manager and falls back to the
    /// supervisory roles — the fail-closed direction, which is why unlinking needs no extra guard.
    /// </summary>
    public void UnlinkFromUser(string updatedBy, DateTimeOffset now)
    {
        if (UserId is null) return;

        UserId = null;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    public void MarkAsActive(string updatedBy, DateTimeOffset now)
    {
        if (Status == PayeeStatus.Active) return;

        Status = PayeeStatus.Active;
        TerminationDate = null;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    public void MarkAsOnLeave(string updatedBy, DateTimeOffset now)
    {
        if (Status == PayeeStatus.Terminated)
            throw new DomainException("Terminated payees cannot be set to On Leave directly. Mark as Active first.");
        if (Status == PayeeStatus.OnLeave) return;

        Status = PayeeStatus.OnLeave;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    public void MarkAsTerminated(DateOnly terminationDate, string updatedBy, DateTimeOffset now)
    {
        if (Status == PayeeStatus.Terminated) return;

        Status = PayeeStatus.Terminated;
        TerminationDate = terminationDate;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    // Decision G: platform deactivation — blocks new transaction assignment.
    // Does not change PayeeStatus (HR status). History corrections remain allowed.
    public void Deactivate(string updatedBy, DateTimeOffset now)
    {
        if (!IsActive) return;

        IsActive = false;
        DeactivatedAt = now;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    public void Activate(string updatedBy, DateTimeOffset now)
    {
        if (IsActive) return;

        IsActive = true;
        DeactivatedAt = null;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }
}
