using System.Globalization;
using Backoffice.Domain.Cases;

namespace Backoffice.Application.Cases;

public sealed record MoneyDto(string Currency, string Amount);

public sealed record CreateCaseRequest(
    string ExternalReference,
    DisputeType DisputeType,
    Channel Channel,
    Priority Priority,
    MoneyDto DisputedAmount);

public sealed record CaseResponse(
    Guid CaseId,
    string TenantId,
    string ExternalReference,
    DisputeType DisputeType,
    Channel Channel,
    CaseState State,
    long CaseVersion,
    Priority Priority,
    MoneyDto DisputedAmount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TimelineEntryResponse(
    Guid Id,
    long CaseVersion,
    string EventType,
    string ActorId,
    string Origin,
    Guid CorrelationId,
    Guid? CausationId,
    string Reason,
    DateTimeOffset OccurredAt,
    IReadOnlyList<string> RuleReferences,
    string? PolicyAction);

public static class CaseMapping
{
    public static CaseResponse ToResponse(this Case @case) => new(
        @case.CaseId,
        @case.TenantId,
        @case.ExternalReference,
        @case.DisputeType,
        @case.Channel,
        @case.State,
        @case.CaseVersion,
        @case.Priority,
        new MoneyDto(@case.DisputedAmount.Currency, @case.DisputedAmount.Amount.ToString("F2", CultureInfo.InvariantCulture)),
        @case.CreatedAt,
        @case.UpdatedAt);

    public static TimelineEntryResponse ToResponse(this TimelineEntry entry) => new(
        entry.Id,
        entry.CaseVersion,
        entry.EventType,
        entry.ActorId,
        entry.Origin,
        entry.CorrelationId,
        entry.CausationId,
        entry.Reason,
        entry.OccurredAt,
        entry.RuleReferences,
        entry.PolicyAction);
}
