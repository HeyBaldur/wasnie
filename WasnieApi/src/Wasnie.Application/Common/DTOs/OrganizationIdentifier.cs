namespace Wasnie.Application.Common.DTOs;

/// <summary>
/// One workspace, as the recovery email names it (KAN-93).
///
/// ★★ THE NAME TRAVELS WITH THE SLUG, AND WITHOUT IT THE EMAIL IS USELESS TO ITS ONE READER. Somebody
/// who administers two workspaces receives two identifiers; a message listing "acme-corp" and
/// "acme-polska" and nothing else asks them to guess which is which, which is the same problem they
/// wrote in about. The company name is what makes the identifier recognisable.
///
/// ★ THE TENANT ID IS HERE FOR THE AUDIT ENTRY, NOT FOR THE EMAIL. It never appears in the message:
/// an internal identifier printed at a user is noise at best and something to paste into a support
/// ticket at worst. It exists so the request can be recorded against the workspace it was about.
/// </summary>
public sealed record OrganizationIdentifier(Guid TenantId, string Name, string Slug);
