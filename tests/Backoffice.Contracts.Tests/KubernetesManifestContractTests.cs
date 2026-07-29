using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Backoffice.Contracts.Tests;

/// <summary>
/// Treats the Kubernetes base as an executable deployment contract. These checks keep
/// workload coverage, digest pinning and the Restricted Pod Security controls from drifting
/// while kubectl validates that the full Kustomize base still renders in CI.
/// </summary>
public sealed class KubernetesManifestContractTests
{
    private const string Namespace = "intelligent-backoffice";
    private static readonly string KubernetesRoot =
        Path.Combine(FindRepositoryRoot(), "deploy", "kubernetes", "base");
    private static readonly Regex DigestImagePattern = new(
        @"^ghcr\.io/[a-z0-9._/-]+@sha256:[0-9a-f]{64}$",
        RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ExpectedWorkloads =
    [
        "intelligent-backoffice-api",
        "intelligent-backoffice-document-intelligence",
        "intelligent-backoffice-outbox-dispatcher",
        "intelligent-backoffice-workflow-worker",
        "intelligent-backoffice-timer-worker",
    ];

    [Fact]
    public void Deployments_CoverEveryRuntimeAndKeepRestrictedSecurityControls()
    {
        var deployments = ReadDocuments()
            .Where(document => Scalar(document, "kind") == "Deployment")
            .ToDictionary(MetadataName, StringComparer.Ordinal);

        Assert.True(
            ExpectedWorkloads.SetEquals(deployments.Keys),
            $"Unexpected Deployment inventory: {string.Join(", ", deployments.Keys.Order())}");

        foreach (var (name, deployment) in deployments)
        {
            Assert.Equal(Namespace, Scalar(Map(deployment, "metadata"), "namespace"));

            var podSpec = Map(Map(Map(deployment, "spec"), "template"), "spec");
            Assert.Equal("false", Scalar(podSpec, "automountServiceAccountToken"));

            var podSecurity = Map(podSpec, "securityContext");
            Assert.Equal("true", Scalar(podSecurity, "runAsNonRoot"));
            Assert.True(int.Parse(Scalar(podSecurity, "runAsUser")) > 0);
            Assert.Equal(
                "RuntimeDefault",
                Scalar(Map(podSecurity, "seccompProfile"), "type"));

            var containers = Sequence(podSpec, "containers").Children
                .Select(node => Assert.IsType<YamlMappingNode>(node))
                .ToArray();
            Assert.NotEmpty(containers);

            foreach (var container in containers)
            {
                var image = Scalar(container, "image");
                Assert.Matches(DigestImagePattern, image);

                var containerSecurity = Map(container, "securityContext");
                Assert.Equal("false", Scalar(containerSecurity, "allowPrivilegeEscalation"));
                Assert.Equal("true", Scalar(containerSecurity, "readOnlyRootFilesystem"));
                Assert.Equal("true", Scalar(containerSecurity, "runAsNonRoot"));
                Assert.Contains(
                    "ALL",
                    Sequence(Map(containerSecurity, "capabilities"), "drop")
                        .Children.Select(Scalar));

                var resources = Map(container, "resources");
                Assert.NotEmpty(Map(resources, "requests").Children);
                Assert.NotEmpty(Map(resources, "limits").Children);
                _ = Map(container, "readinessProbe");
                _ = Map(container, "livenessProbe");
            }
        }
    }

    [Fact]
    public void Namespace_EnforcesRestrictedPodSecurityAtPinnedKubernetesVersion()
    {
        var namespace = ReadDocuments()
            .Single(document => Scalar(document, "kind") == "Namespace");
        var labels = Map(Map(namespace, "metadata"), "labels");

        foreach (var mode in new[] { "enforce", "audit", "warn" })
        {
            Assert.Equal("restricted", Scalar(labels, $"pod-security.kubernetes.io/{mode}"));
            Assert.Equal("v1.36", Scalar(labels, $"pod-security.kubernetes.io/{mode}-version"));
        }
    }

    [Fact]
    public void NetworkPolicies_CoverEveryRuntimeWorkload()
    {
        var protectedWorkloads = ReadDocuments()
            .Where(document => Scalar(document, "kind") == "NetworkPolicy")
            .Select(document => Scalar(
                Map(Map(Map(document, "spec"), "podSelector"), "matchLabels"),
                "app.kubernetes.io/name"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            ExpectedWorkloads.SetEquals(protectedWorkloads),
            $"Unexpected NetworkPolicy coverage: {string.Join(", ", protectedWorkloads.Order())}");
    }

    [Fact]
    public void RuntimeConfiguration_RoutesApiToDocumentIntelligenceService()
    {
        var configMap = ReadDocuments()
            .Single(document => Scalar(document, "kind") == "ConfigMap");
        var data = Map(configMap, "data");

        Assert.Equal(
            "http://intelligent-backoffice-document-intelligence:8080",
            Scalar(data, "DocumentIntelligence__BaseUrl"));

        var services = ReadDocuments()
            .Where(document => Scalar(document, "kind") == "Service")
            .Select(MetadataName)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("intelligent-backoffice-document-intelligence", services);
    }

    private static IEnumerable<YamlMappingNode> ReadDocuments()
    {
        foreach (var path in Directory.EnumerateFiles(KubernetesRoot, "*.yaml"))
        {
            using var reader = File.OpenText(path);
            var stream = new YamlStream();
            stream.Load(reader);

            foreach (var document in stream.Documents)
            {
                yield return Assert.IsType<YamlMappingNode>(document.RootNode);
            }
        }
    }

    private static string MetadataName(YamlMappingNode document) =>
        Scalar(Map(document, "metadata"), "name");

    private static YamlMappingNode Map(YamlMappingNode mapping, string key) =>
        Assert.IsType<YamlMappingNode>(Required(mapping, key));

    private static YamlSequenceNode Sequence(YamlMappingNode mapping, string key) =>
        Assert.IsType<YamlSequenceNode>(Required(mapping, key));

    private static string Scalar(YamlMappingNode mapping, string key) =>
        Scalar(Required(mapping, key));

    private static string Scalar(YamlNode node) =>
        Assert.IsType<YamlScalarNode>(node).Value
        ?? throw new InvalidDataException("Expected a non-null YAML scalar.");

    private static YamlNode Required(YamlMappingNode mapping, string key)
    {
        Assert.True(
            mapping.Children.TryGetValue(new YamlScalarNode(key), out var node),
            $"Required YAML key '{key}' was not found.");
        return node!;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Backoffice.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate Backoffice.sln above '{AppContext.BaseDirectory}'.");
    }
}
