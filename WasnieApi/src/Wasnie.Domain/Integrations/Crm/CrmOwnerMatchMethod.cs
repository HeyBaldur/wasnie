namespace Wasnie.Domain.Integrations.Crm;

/// <summary>How a CRM owner came to be linked to a Wasnie payee. Recorded for audit/explainability.</summary>
public enum CrmOwnerMatchMethod
{
    /// <summary>Auto-matched because the owner's normalized email equalled exactly one payee's email.</summary>
    Email = 0,

    /// <summary>Manually linked by a user via the mapping queue (the owner did not auto-resolve).</summary>
    Manual = 1,
}
