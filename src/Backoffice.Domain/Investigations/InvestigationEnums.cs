namespace Backoffice.Domain.Investigations;

public enum InvestigationStatus
{
    Pending,
    Running,
    Completed,
    Failed,
}

public enum FindingKind
{
    ConfirmedFact,
    Inference,
    Divergence,
    MissingData,
}
