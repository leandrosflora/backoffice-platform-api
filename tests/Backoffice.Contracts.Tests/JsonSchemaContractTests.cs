using System.Text.Json;
using System.Text.Json.Serialization;
using Backoffice.Api;
using Backoffice.Application.Cases;
using Backoffice.Domain.Cases;
using Json.Schema;
using YamlDotNet.Serialization;

namespace Backoffice.Contracts.Tests;

/// <summary>
/// Validates real, actually-serialized API response/request payloads against the
/// authoritative JSON-Schema draft 2020-12 definitions in
/// `contracts/schemas/canonical-models-base.yaml`, using a real schema validator
/// (JsonSchema.Net), not a hand-rolled field-by-field comparison (spec: platform-deployment,
/// task 13.2). Uses the base (non-indirection) schema file directly —
/// `canonical-models.yaml`'s own `$ref`s point at a `$id` that doesn't match its own
/// filename (a pre-existing authoring inconsistency in the reference repo, unrelated to this
/// port), which would otherwise require a brittle custom resolver just to work around a spec
/// bug we don't own.
/// </summary>
public class JsonSchemaContractTests
{
    private static readonly JsonSerializerOptions ApiJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(ScreamingSnakeCaseNamingPolicy.Instance) },
    };

    /// <summary>
    /// Loads the whole base document (so sibling `$defs` like Money/UUID/Timestamp stay
    /// resolvable via local "#/$defs/..." refs) and points a top-level `$ref` at the
    /// requested definition — draft 2020-12 evaluates `$ref` alongside sibling keywords, so
    /// this validates the instance against exactly that `$defs` entry, not the document's
    /// no-op top-level `type: object`.
    /// </summary>
    private static JsonSchema ReadCanonicalModelsDef(string defName)
    {
        var path = Path.Combine(ArchitectureRepoLocator.FindContractsRoot(), "schemas", "canonical-models-base.yaml");
        var yaml = File.ReadAllText(path);

        // YamlDotNet's Deserialize<object>() deliberately keeps every scalar as a plain
        // string (it has no schema to infer "1" is an int or "false" is a bool from) — fine
        // for the string-only checks elsewhere in this project, but JsonSchema.Net's
        // "minimum"/"additionalProperties" keywords require real JSON number/bool values.
        // FixYamlScalarTypes recovers them before handing the tree to System.Text.Json.
        var yamlObject = new DeserializerBuilder().Build().Deserialize<object>(yaml);
        var node = FixYamlScalarTypes(yamlObject);
        var obj = node!.AsObject();
        // JsonSchema.Net registers a document globally by its "$id" the first time it's
        // built; building this same file's $id twice (once per [Fact] in this class) would
        // otherwise throw "Overwriting registered schemas is not permitted" on the second
        // call. No cross-document resolution is needed here — only local "#/$defs/..."
        // fragment refs — so dropping $id avoids the collision entirely.
        obj.Remove("$id");
        obj["$ref"] = $"#/$defs/{defName}";

        return JsonSchema.FromText(obj.ToJsonString());
    }

    private static System.Text.Json.Nodes.JsonNode? FixYamlScalarTypes(object? value) => value switch
    {
        null => null,
        Dictionary<object, object> map => new System.Text.Json.Nodes.JsonObject(
            map.Select(pair => KeyValuePair.Create((string)pair.Key, FixYamlScalarTypes(pair.Value)))),
        List<object> list => new System.Text.Json.Nodes.JsonArray(list.Select(FixYamlScalarTypes).ToArray()),
        "true" => System.Text.Json.Nodes.JsonValue.Create(true),
        "false" => System.Text.Json.Nodes.JsonValue.Create(false),
        string s when long.TryParse(s, out var l) => System.Text.Json.Nodes.JsonValue.Create(l),
        string s => System.Text.Json.Nodes.JsonValue.Create(s),
        _ => throw new NotSupportedException($"Unexpected YAML scalar CLR type: {value.GetType()}"),
    };

    private static EvaluationResults Evaluate(JsonSchema schema, JsonElement instance) =>
        schema.Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical });

    private static void AssertConforms(JsonSchema schema, JsonElement instance)
    {
        var results = Evaluate(schema, instance);
        Assert.True(
            results.IsValid,
            "Schema validation failed: " + System.Text.Json.JsonSerializer.Serialize(results));
    }

    [Fact]
    public void CaseResponse_RealSerializedPayload_ConformsToCanonicalCaseSchema()
    {
        var schema = ReadCanonicalModelsDef("Case");

        var caseResponse = new CaseResponse(
            Guid.NewGuid(), "tenant-a", "ext-1", DisputeType.CardPurchase, Channel.App,
            CaseState.Created, 1, Priority.Normal, new MoneyDto("BRL", "150.00"),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var instance = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(caseResponse, ApiJsonOptions)).RootElement;

        AssertConforms(schema, instance);
    }

    /// <summary>
    /// Genuine, pre-existing non-conformance (not a bug introduced by this change): our
    /// <see cref="CreateCaseRequest"/> lets callers set `priority` at creation time, but the
    /// documented `CreateCaseRequest` schema doesn't list `priority` among its properties and
    /// declares `additionalProperties: false`, so a real serialized request fails validation.
    /// Recorded here as a discovered gap (spec: platform-deployment, task 13.2) rather than
    /// silently dropping the `priority` field from case creation, which sections 1–2 already
    /// established as intended behavior and which many existing tests rely on.
    /// </summary>
    [Fact]
    public void CreateCaseRequest_RealSerializedPayload_FailsCanonicalSchema_DueToUndocumentedPriorityField()
    {
        var schema = ReadCanonicalModelsDef("CreateCaseRequest");

        var request = new CreateCaseRequest("ext-1", DisputeType.Pix, Channel.Web, Priority.High, new MoneyDto("BRL", "99.90"));
        var instance = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(request, ApiJsonOptions)).RootElement;

        var results = Evaluate(schema, instance);
        Assert.False(results.IsValid);
    }
}
