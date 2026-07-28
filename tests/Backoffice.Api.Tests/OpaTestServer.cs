using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.Sockets;

namespace Backoffice.Api.Tests;

/// <summary>
/// Runs the real, unmodified policies/authorization.rego against a standalone OPA server
/// process (opa.exe run --server), so tests exercise the actual policy decision point
/// rather than a stub (spec: policy-authorization — the Rego is reused unmodified).
/// </summary>
public sealed class OpaTestServer : IDisposable
{
    private const string OpaExecutableEnvVar = "OPA_EXECUTABLE";
    private static readonly string DefaultOpaExecutable = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "bin", "opa.exe");

    private readonly Process _process;

    public string BaseUrl { get; }

    public string? LogPath { get; private init; }

    private OpaTestServer(Process process, string baseUrl)
    {
        _process = process;
        BaseUrl = baseUrl;
    }

    public static async Task<OpaTestServer> StartAsync()
    {
        var opaExecutable = Environment.GetEnvironmentVariable(OpaExecutableEnvVar) ?? DefaultOpaExecutable;
        if (!File.Exists(opaExecutable))
        {
            throw new FileNotFoundException(
                $"OPA executable not found at '{opaExecutable}'. Set the {OpaExecutableEnvVar} environment variable to override.", opaExecutable);
        }

        var policyPath = FindPolicyFile();
        var port = GetFreeTcpPort();
        var baseUrl = $"http://localhost:{port}";

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = opaExecutable,
                Arguments = $"run --server --addr :{port} --log-level debug \"{policyPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        var logPath = Path.Combine(Path.GetTempPath(), $"opa-test-{port}.log");
        var logWriter = new StreamWriter(logPath, append: false) { AutoFlush = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) logWriter.WriteLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) logWriter.WriteLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await WaitUntilReadyAsync(baseUrl, process);

        return new OpaTestServer(process, baseUrl) { LogPath = logPath };
    }

    private static async Task WaitUntilReadyAsync(string baseUrl, Process process)
    {
        using var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(15);

        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException($"OPA process exited early with code {process.ExitCode}.");
            }

            try
            {
                var response = await client.PostAsJsonAsync(
                    "v1/data/intelligent_backoffice/authorization/decision",
                    new { input = new { subject = new { }, resource = new { } } });
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not up yet; retry.
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("OPA test server did not become ready in time.");
    }

    private static string FindPolicyFile()
    {
        const string repoDirectoryName = "intelligent-backoffice-platform-architecture";
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, repoDirectoryName, "policies", "authorization.rego");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate a sibling '{repoDirectoryName}/policies/authorization.rego' above '{AppContext.BaseDirectory}'.");
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(2000);
        }

        _process.Dispose();
    }
}
