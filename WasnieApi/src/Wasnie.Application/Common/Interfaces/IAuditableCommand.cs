namespace Wasnie.Application.Common.Interfaces;

public interface IAuditableCommand
{
    string AuditAction { get; }
    string AuditResourceType { get; }
    string? AuditResourceId { get; }
    string? AuditDisplayName { get; }
}
