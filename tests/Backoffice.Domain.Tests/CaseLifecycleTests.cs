using Backoffice.Domain.Cases;

namespace Backoffice.Domain.Tests;

public class CaseLifecycleTests
{
    [Theory]
    [InlineData(CaseState.Created, CaseState.DocumentsReceived, true)]
    [InlineData(CaseState.Created, CaseState.Approved, false)]
    [InlineData(CaseState.AwaitingApproval, CaseState.Approved, true)]
    [InlineData(CaseState.AwaitingApproval, CaseState.MoreEvidenceRequired, true)]
    [InlineData(CaseState.Approved, CaseState.Executed, false)]
    [InlineData(CaseState.ExecutionPending, CaseState.ReconciliationRequired, true)]
    [InlineData(CaseState.Closed, CaseState.Created, false)]
    public void CanTransition_MatchesDocumentedGraph(CaseState from, CaseState to, bool expected)
    {
        Assert.Equal(expected, CaseLifecycle.CanTransition(from, to));
    }

    [Theory]
    [InlineData(CaseState.Closed)]
    [InlineData(CaseState.Cancelled)]
    public void IsTerminal_ReturnsTrueForTerminalStates(CaseState state)
    {
        Assert.True(CaseLifecycle.IsTerminal(state));
    }

    [Fact]
    public void IsTerminal_ReturnsFalseForNonTerminalState()
    {
        Assert.False(CaseLifecycle.IsTerminal(CaseState.Created));
    }
}
