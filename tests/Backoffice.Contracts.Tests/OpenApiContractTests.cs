using YamlDotNet.Serialization;

namespace Backoffice.Contracts.Tests;

/// <summary>
/// Smoke-checks that the authoritative OpenAPI contract this codebase implements against
/// still parses as valid YAML, declares OpenAPI 3.1, and still lists the case-management
/// paths section 2 targets. Parsed generically (not into the OpenAPI object model) because
/// the mainstream Microsoft.OpenApi.Readers package does not yet support OpenAPI 3.1, which
/// this contract uses; deeper schema/response-shape conformance checks land in later
/// sections once a 3.1-capable validator is chosen.
/// </summary>
public class OpenApiContractTests
{
    private static string ContractsRoot => ArchitectureRepoLocator.FindContractsRoot();

    private static Dictionary<object, object> ReadPlatformApi()
    {
        var path = Path.Combine(ContractsRoot, "openapi", "platform-api.yaml");
        var yaml = File.ReadAllText(path);

        var deserializer = new DeserializerBuilder().Build();
        return deserializer.Deserialize<Dictionary<object, object>>(yaml);
    }

    [Fact]
    public void PlatformApi_DeclaresOpenApi31()
    {
        var document = ReadPlatformApi();

        Assert.Equal("3.1.0", document["openapi"]);
    }

    [Theory]
    [InlineData("/v1/cases")]
    [InlineData("/v1/cases/{caseId}")]
    [InlineData("/v1/cases/{caseId}/cancel")]
    [InlineData("/v1/cases/{caseId}/timeline")]
    [InlineData("/v1/cases/{caseId}/documents")]
    [InlineData("/v1/cases/{caseId}/documents/{documentId}")]
    [InlineData("/v1/cases/{caseId}/evidence")]
    [InlineData("/v1/cases/{caseId}/investigations")]
    [InlineData("/v1/cases/{caseId}/recommendations")]
    [InlineData("/v1/cases/{caseId}/approvals")]
    [InlineData("/v1/cases/{caseId}/executions")]
    [InlineData("/v1/cases/{caseId}/executions/{executionId}")]
    [InlineData("/v1/cases/{caseId}/reconciliations/{executionId}/resolve")]
    public void PlatformApi_DeclaresCaseManagementPath(string path)
    {
        var document = ReadPlatformApi();
        var paths = (Dictionary<object, object>)document["paths"];

        Assert.Contains(path, paths.Keys);
    }

    private static Dictionary<object, object> ReadEventingOperationsApi()
    {
        var path = Path.Combine(ContractsRoot, "openapi", "eventing-operations-api.yaml");
        var yaml = File.ReadAllText(path);

        var deserializer = new DeserializerBuilder().Build();
        return deserializer.Deserialize<Dictionary<object, object>>(yaml);
    }

    [Theory]
    [InlineData("/v1/operations/cases/{caseId}/timers")]
    [InlineData("/v1/operations/outbox")]
    [InlineData("/v1/operations/dead-letters")]
    [InlineData("/v1/operations/dead-letters/{deadLetterId}/replay")]
    public void EventingOperationsApi_DeclaresPath(string path)
    {
        var document = ReadEventingOperationsApi();
        var paths = (Dictionary<object, object>)document["paths"];

        Assert.Contains(path, paths.Keys);
    }

    /// <summary>
    /// `GET /v1/operations/timers` (list all timers, not scoped to a case) is implemented
    /// (`OperationsEndpoints.cs`) but not declared in `eventing-operations-api.yaml` — an
    /// undocumented-but-real extra endpoint, the same kind of doc/implementation gap section
    /// 10 found for `backoffice_cases_created_total`. Documented here rather than silently
    /// dropped or silently left unverified.
    /// </summary>
    [Fact]
    public void EventingOperationsApi_ListAllTimersEndpoint_IsImplementedButUndocumented()
    {
        var document = ReadEventingOperationsApi();
        var paths = (Dictionary<object, object>)document["paths"];

        Assert.DoesNotContain("/v1/operations/timers", paths.Keys);
    }

    [Fact]
    public void PlatformApi_CasesPathRefTargetFileExists()
    {
        var document = ReadPlatformApi();
        var paths = (Dictionary<object, object>)document["paths"];
        var casesPath = (Dictionary<object, object>)paths["/v1/cases"];
        var reference = (string)casesPath["$ref"];

        var referencedFile = reference.Split('#')[0];
        var resolvedPath = Path.GetFullPath(Path.Combine(ContractsRoot, "openapi", referencedFile));

        Assert.True(File.Exists(resolvedPath), $"Referenced contract file not found: {resolvedPath}");
    }
}
