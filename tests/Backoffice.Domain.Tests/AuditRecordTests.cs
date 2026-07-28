using System.Reflection;
using Backoffice.Domain.Audit;

namespace Backoffice.Domain.Tests;

public class AuditRecordTests
{
    [Fact]
    public void Create_PopulatesAllFields()
    {
        var eventId = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;
        var ingestedAt = occurredAt.AddSeconds(1);

        var record = AuditRecord.Create(
            eventId, "DecisionApproved", "tenant-a", aggregateId, correlationId, null,
            "approval.decide", ["BR-012", "BR-014"], "{}", occurredAt, ingestedAt);

        Assert.Equal(eventId, record.EventId);
        Assert.Equal("DecisionApproved", record.EventType);
        Assert.Equal("approval.decide", record.PolicyAction);
        Assert.Equal(["BR-012", "BR-014"], record.RuleReferences);
        Assert.Equal(occurredAt, record.OccurredAt);
        Assert.Equal(ingestedAt, record.IngestedAt);
    }

    /// <summary>
    /// Proves "no update or delete path exposed" (spec: audit-compliance) is a property of
    /// the type itself, not just a convention: reflection finds no public method or settable
    /// property on <see cref="AuditRecord"/> other than the static factory and get-only
    /// properties, so there is no API surface through which a record could be mutated once
    /// created.
    /// </summary>
    [Fact]
    public void AuditRecord_ExposesNoMutationMembers()
    {
        var type = typeof(AuditRecord);

        var publicInstanceMethods = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName) // excludes property get_/set_ accessors
            .ToList();
        Assert.Empty(publicInstanceMethods);

        var settableProperties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true } setMethod && !IsInitOnly(setMethod))
            .ToList();
        Assert.Empty(settableProperties);
    }

    private static bool IsInitOnly(MethodInfo setMethod) =>
        setMethod.ReturnParameter
            .GetRequiredCustomModifiers()
            .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
}
