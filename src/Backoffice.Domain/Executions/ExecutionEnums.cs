namespace Backoffice.Domain.Executions;

public enum CommandType
{
    MockRefund,
    MockReversal,
}

/// <summary>Per contracts/schemas/canonical-models-base.yaml $defs/Execution.</summary>
public enum ExecutionStatus
{
    Pending,
    Succeeded,
    Failed,
    ReconciliationRequired,
    Reconciled,
}

/// <summary>Per contracts/schemas/canonical-models-base.yaml $defs/ReconciliationResolutionRequest.</summary>
public enum ReconciliationResolution
{
    ConfirmedSucceeded,
    ConfirmedFailed,
    Escalated,
}
