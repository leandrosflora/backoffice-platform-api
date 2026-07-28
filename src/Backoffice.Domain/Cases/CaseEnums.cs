namespace Backoffice.Domain.Cases;

public enum DisputeType
{
    CardPurchase,
    Pix,
    Transfer,
    CashWithdrawal,
    Other,
}

public enum Channel
{
    App,
    Web,
    ContactCenter,
    Branch,
    Api,
}

public enum Priority
{
    Low,
    Normal,
    High,
    Critical,
}

/// <summary>
/// Canonical case lifecycle states per docs/functional/case-lifecycle.md (16 states).
/// </summary>
public enum CaseState
{
    Created,
    AwaitingDocuments,
    DocumentsReceived,
    DocumentsValidated,
    UnderInvestigation,
    DecisionProposed,
    AwaitingApproval,
    MoreEvidenceRequired,
    Approved,
    ExecutionPending,
    Executed,
    ReconciliationRequired,
    Rejected,
    Closed,
    Cancelled,
    Expired,
    Failed,
}
