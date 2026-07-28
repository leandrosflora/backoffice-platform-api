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
    public void PlatformApi_DeclaresCaseManagementPath(string path)
    {
        var document = ReadPlatformApi();
        var paths = (Dictionary<object, object>)document["paths"];

        Assert.Contains(path, paths.Keys);
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
