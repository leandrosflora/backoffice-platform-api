using Backoffice.Domain.Cases;
using Backoffice.Domain.Common;

namespace Backoffice.Domain.Tests;

public class CaseTests
{
    private static Case NewCase() => Case.Create(
        tenantId: "tenant-a",
        externalReference: "ext-1",
        disputeType: DisputeType.CardPurchase,
        channel: Channel.App,
        priority: Priority.Normal,
        disputedAmount: new Money("BRL", 100m),
        correlationId: Guid.NewGuid(),
        actorId: "actor-1",
        now: DateTimeOffset.UtcNow);

    [Fact]
    public void Create_StartsInCreatedStateWithVersionOne()
    {
        var @case = NewCase();

        Assert.Equal(CaseState.Created, @case.State);
        Assert.Equal(1, @case.CaseVersion);
        Assert.Single(@case.Timeline);
    }

    [Fact]
    public void Transition_ToInvalidState_Throws()
    {
        var @case = NewCase();

        Assert.Throws<InvalidCaseTransitionException>(() =>
            @case.Transition(
                expectedVersion: @case.CaseVersion,
                to: CaseState.Approved,
                eventType: "Invalid",
                actorId: "actor-1",
                origin: "test",
                correlationId: Guid.NewGuid(),
                causationId: null,
                reason: "should fail",
                now: DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Transition_WithStaleVersion_ThrowsConflict()
    {
        var @case = NewCase();

        Assert.Throws<CaseVersionConflictException>(() =>
            @case.Transition(
                expectedVersion: @case.CaseVersion - 1,
                to: CaseState.DocumentsReceived,
                eventType: "DocumentsReceived",
                actorId: "actor-1",
                origin: "test",
                correlationId: Guid.NewGuid(),
                causationId: null,
                reason: "stale",
                now: DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Transition_Valid_IncrementsVersionAndAppendsTimeline()
    {
        var @case = NewCase();
        var expectedVersion = @case.CaseVersion;

        @case.Transition(
            expectedVersion,
            CaseState.DocumentsReceived,
            "DocumentReceived",
            "actor-1",
            "documents",
            Guid.NewGuid(),
            null,
            "first document received",
            DateTimeOffset.UtcNow);

        Assert.Equal(CaseState.DocumentsReceived, @case.State);
        Assert.Equal(expectedVersion + 1, @case.CaseVersion);
        Assert.Equal(2, @case.Timeline.Count);
    }
}
