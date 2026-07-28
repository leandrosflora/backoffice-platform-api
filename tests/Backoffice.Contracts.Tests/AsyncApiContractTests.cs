using Backoffice.Domain.Cases;
using YamlDotNet.Serialization;

namespace Backoffice.Contracts.Tests;

/// <summary>
/// Confirms every wire-format event type this codebase actually emits
/// (<see cref="EventTypes"/>) matches a channel `address` declared in
/// `contracts/asyncapi/platform-events.yaml` exactly (spec: platform-deployment, task 13.2).
/// Found and fixed a real bug via this check: every event type used to be a bare PascalCase
/// name ("CaseCreated") with no relation at all to the documented dotted/versioned wire
/// format ("backoffice.case.created.v1") — see `EventTypes`'s own doc comment for the fix.
/// </summary>
public class AsyncApiContractTests
{
    private static Dictionary<object, object> ReadPlatformEvents()
    {
        var path = Path.Combine(ArchitectureRepoLocator.FindContractsRoot(), "asyncapi", "platform-events.yaml");
        var yaml = File.ReadAllText(path);

        var deserializer = new DeserializerBuilder().Build();
        return deserializer.Deserialize<Dictionary<object, object>>(yaml);
    }

    private static HashSet<string> ReadChannelAddresses()
    {
        var document = ReadPlatformEvents();
        var channels = (Dictionary<object, object>)document["channels"];
        return channels.Values
            .Cast<Dictionary<object, object>>()
            .Select(channel => (string)channel["address"])
            .ToHashSet();
    }

    [Theory]
    [InlineData(nameof(EventTypes.CaseCreated))]
    [InlineData(nameof(EventTypes.DocumentReceived))]
    [InlineData(nameof(EventTypes.DocumentValidated))]
    [InlineData(nameof(EventTypes.EvidenceMissing))]
    [InlineData(nameof(EventTypes.DecisionProposed))]
    [InlineData(nameof(EventTypes.ApprovalRequested))]
    [InlineData(nameof(EventTypes.DecisionApproved))]
    [InlineData(nameof(EventTypes.DecisionRejected))]
    [InlineData(nameof(EventTypes.ExecutionRequested))]
    [InlineData(nameof(EventTypes.ExecutionCompleted))]
    [InlineData(nameof(EventTypes.ExecutionFailed))]
    [InlineData(nameof(EventTypes.ReconciliationRequired))]
    public void EventTypes_Constant_MatchesADeclaredChannelAddress(string constantName)
    {
        var value = (string)typeof(EventTypes).GetField(constantName)!.GetValue(null)!;
        var addresses = ReadChannelAddresses();

        Assert.Contains(value, addresses);
    }

    /// <summary>
    /// Four case-lifecycle events documented in the asyncapi/event-envelope contracts
    /// (`investigation.completed`, `case.closed`) have no corresponding code path at all —
    /// not a wire-format bug like the ones fixed above, but a genuine feature gap versus the
    /// documented catalog. Left unimplemented deliberately: closing it means designing new
    /// transitions (e.g., what fires `case.closed` after Executed/Rejected/Failed/Expired)
    /// that were never scoped in tasks.md's sections 5–9, so it's recorded here rather than
    /// silently added in a "final conformance pass".
    /// </summary>
    [Theory]
    [InlineData("backoffice.investigation.completed.v1")]
    [InlineData("backoffice.case.closed.v1")]
    public void DocumentedEvent_HasNoCorrespondingImplementation(string address)
    {
        var implementedAddresses = new[]
        {
            EventTypes.CaseCreated, EventTypes.DocumentReceived, EventTypes.DocumentValidated,
            EventTypes.EvidenceMissing, EventTypes.DecisionProposed, EventTypes.ApprovalRequested,
            EventTypes.DecisionApproved, EventTypes.DecisionRejected, EventTypes.ExecutionRequested,
            EventTypes.ExecutionCompleted, EventTypes.ExecutionFailed, EventTypes.ReconciliationRequired,
        };

        Assert.DoesNotContain(address, implementedAddresses);
        Assert.Contains(address, ReadChannelAddresses());
    }
}
