using Backoffice.Domain.Common;

namespace Backoffice.Domain.Cases;

/// <summary>
/// Aggregate root for a backoffice dispute case. Every mutation goes through
/// a method that validates the lifecycle transition and appends a timeline entry,
/// so the aggregate and its audit trail can never drift apart.
/// </summary>
public sealed class Case
{
    /// <summary>Default window a case may sit in AWAITING_APPROVAL before auto-expiring.</summary>
    public static readonly TimeSpan ApprovalWindow = TimeSpan.FromHours(24);

    public Guid CaseId { get; private init; }
    public string TenantId { get; private init; } = string.Empty;
    public string ExternalReference { get; private init; } = string.Empty;
    public DisputeType DisputeType { get; private init; }
    public Channel Channel { get; private init; }
    public CaseState State { get; private set; }
    public long CaseVersion { get; private set; }
    public Priority Priority { get; private init; }
    public Money DisputedAmount { get; private init; } = Money.Zero("BRL");
    public string? RecommendationActorId { get; private set; }
    public long? RecommendationVersion { get; private set; }
    public long? ApprovedRecommendationVersion { get; private set; }

    /// <summary>
    /// When set and passed with no decision recorded, the case auto-expires out of
    /// AWAITING_APPROVAL (spec: human-approval, "More-evidence-required loop and approval
    /// expiry"). Distinct from Approval.ExpiresAt, which is the validity window of an
    /// already-made decision, not a deadline to decide.
    /// </summary>
    public DateTimeOffset? ApprovalDeadline { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Append-only in practice: nothing outside this class ever calls anything but Add
    /// on it (enforced by the domain API surface, not by the CLR type), so EF Core can
    /// track it as an ordinary mutable collection navigation.
    /// </summary>
    public ICollection<TimelineEntry> Timeline { get; private init; } = new List<TimelineEntry>();

    private Case() { }

    public static Case Create(
        string tenantId,
        string externalReference,
        DisputeType disputeType,
        Channel channel,
        Priority priority,
        Money disputedAmount,
        Guid correlationId,
        string actorId,
        DateTimeOffset now)
    {
        var @case = new Case
        {
            CaseId = Guid.NewGuid(),
            TenantId = tenantId,
            ExternalReference = externalReference,
            DisputeType = disputeType,
            Channel = channel,
            State = CaseState.Created,
            CaseVersion = 1,
            Priority = priority,
            DisputedAmount = disputedAmount,
            CreatedAt = now,
            UpdatedAt = now,
        };

        @case.Timeline.Add(TimelineEntry.Create(
            @case.CaseId,
            @case.CaseVersion,
            "CaseCreated",
            actorId,
            "case-intake",
            correlationId,
            null,
            "Case created via intake.",
            now));

        return @case;
    }

    /// <summary>
    /// Applies a lifecycle transition, enforcing optimistic concurrency against
    /// <paramref name="expectedVersion"/> and validating the transition is allowed.
    /// </summary>
    public void Transition(
        long expectedVersion,
        CaseState to,
        string eventType,
        string actorId,
        string origin,
        Guid correlationId,
        Guid? causationId,
        string reason,
        DateTimeOffset now)
    {
        if (expectedVersion != CaseVersion)
        {
            throw new CaseVersionConflictException(expectedVersion, CaseVersion);
        }

        if (!CaseLifecycle.CanTransition(State, to))
        {
            throw new InvalidCaseTransitionException(State, to);
        }

        State = to;
        CaseVersion++;
        UpdatedAt = now;

        Timeline.Add(TimelineEntry.Create(
            CaseId,
            CaseVersion,
            eventType,
            actorId,
            origin,
            correlationId,
            causationId,
            reason,
            now));
    }

    public void RecordRecommendation(long recommendationVersion, string recommendationActorId)
    {
        RecommendationVersion = recommendationVersion;
        RecommendationActorId = recommendationActorId;
    }

    public void RecordApproval(long approvedRecommendationVersion)
    {
        ApprovedRecommendationVersion = approvedRecommendationVersion;
    }

    public void SetApprovalDeadline(DateTimeOffset deadline)
    {
        ApprovalDeadline = deadline;
    }

    public void ClearApprovalDeadline()
    {
        ApprovalDeadline = null;
    }

    /// <summary>
    /// Transitions AWAITING_APPROVAL -> EXPIRED if the deadline has passed with no decision
    /// yet recorded. Returns true when it expired the case, so callers know to persist the
    /// change. On-read evaluation stands in for a dedicated background worker until section 8.
    /// </summary>
    public bool ExpireApprovalIfDue(DateTimeOffset now, Guid correlationId)
    {
        if (State != CaseState.AwaitingApproval || ApprovalDeadline is null || now < ApprovalDeadline.Value)
        {
            return false;
        }

        Transition(
            CaseVersion, CaseState.Expired, "ApprovalExpired", "system", "approval-expiry",
            correlationId, null, "Approval deadline passed with no decision recorded.", now);
        ApprovalDeadline = null;
        return true;
    }

    /// <summary>
    /// Fired by the timer worker when a `CASE_EXPIRY` timer becomes due (spec:
    /// eventing-reliability, "Case expiry timer transitions a stale case"). Unlike
    /// <see cref="ExpireApprovalIfDue"/> (scoped to the approval-window deadline), this
    /// applies to any state from which EXPIRED is a valid transition per
    /// <see cref="CaseLifecycle"/> — a case that has since moved past such a state (e.g.
    /// already cancelled or executed) is left untouched rather than throwing.
    /// </summary>
    public bool ExpireIfEligible(Guid correlationId, DateTimeOffset now)
    {
        if (!CaseLifecycle.CanTransition(State, CaseState.Expired))
        {
            return false;
        }

        Transition(
            CaseVersion, CaseState.Expired, "CaseExpired", "timer-worker", "case-expiry",
            correlationId, null, "Case expiry timer fired.", now);
        return true;
    }
}
