namespace Backoffice.Domain.Cases;

public sealed class InvalidCaseTransitionException(CaseState from, CaseState to)
    : Exception($"Transition from '{from}' to '{to}' is not allowed by the case lifecycle.")
{
    public CaseState From { get; } = from;
    public CaseState To { get; } = to;
}

public sealed class CaseVersionConflictException(long expectedVersion, long actualVersion)
    : Exception($"Expected case version {expectedVersion} but current version is {actualVersion}.")
{
    public long ExpectedVersion { get; } = expectedVersion;
    public long ActualVersion { get; } = actualVersion;
}
