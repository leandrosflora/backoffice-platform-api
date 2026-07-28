namespace Backoffice.Application.Cases;

/// <summary>
/// Thrown when a case does not exist OR exists but belongs to a different tenant.
/// Handlers must map this to 404, never 403, to avoid leaking cross-tenant existence
/// (spec: case-management, "Cross-tenant case isolation").
/// </summary>
public sealed class CaseNotFoundException(Guid caseId) : Exception($"Case '{caseId}' was not found.")
{
    public Guid CaseId { get; } = caseId;
}
