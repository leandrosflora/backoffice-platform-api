using System.Diagnostics;
using System.Net.Sockets;
using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace Backoffice.Workers.Tests;

/// <summary>
/// Runs a real, single-node Kafka broker (KRaft mode, no Zookeeper) in a disposable Docker
/// container for the duration of a test class, so eventing tests exercise the actual
/// broker/consumer-group/offset-commit semantics rather than a stub (spec:
/// eventing-reliability — Kafka/Redpanda is reused external infrastructure, per design.md).
/// </summary>
public sealed class KafkaTestBroker : IAsyncDisposable
{
    // A fixed, syntactically valid (22-char, url-safe base64) KRaft cluster id — safe to
    // reuse across ephemeral single-node test containers, each with its own throwaway
    // in-container storage.
    private const string ClusterId = "rDamCnDPTyCt49uuavbNGw";
    private const string Image = "apache/kafka:3.9.2";

    private readonly string _containerName;
    public string BootstrapServers { get; }

    private KafkaTestBroker(string containerName, int port)
    {
        _containerName = containerName;
        BootstrapServers = $"localhost:{port}";
    }

    public static async Task<KafkaTestBroker> StartAsync()
    {
        var port = GetFreeTcpPort();
        var containerName = $"backoffice-test-kafka-{Guid.NewGuid():N}";

        var args = new[]
        {
            "run", "-d", "--rm",
            "--name", containerName,
            "-p", $"{port}:9092",
            "-e", "KAFKA_NODE_ID=1",
            "-e", "KAFKA_PROCESS_ROLES=broker,controller",
            "-e", "KAFKA_LISTENERS=PLAINTEXT://:9092,CONTROLLER://:9093",
            "-e", $"KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://localhost:{port}",
            "-e", "KAFKA_CONTROLLER_LISTENER_NAMES=CONTROLLER",
            "-e", "KAFKA_CONTROLLER_QUORUM_VOTERS=1@localhost:9093",
            "-e", "KAFKA_LISTENER_SECURITY_PROTOCOL_MAP=CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT",
            "-e", "KAFKA_INTER_BROKER_LISTENER_NAME=PLAINTEXT",
            "-e", "KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1",
            "-e", "KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR=1",
            "-e", "KAFKA_TRANSACTION_STATE_LOG_MIN_ISR=1",
            "-e", "KAFKA_AUTO_CREATE_TOPICS_ENABLE=true",
            "-e", $"CLUSTER_ID={ClusterId}",
            Image,
        };

        await RunDockerAsync(args, $"start Kafka test container '{containerName}'");

        var broker = new KafkaTestBroker(containerName, port);
        try
        {
            await broker.WaitUntilReadyAsync();
        }
        catch
        {
            await broker.DisposeAsync();
            throw;
        }

        return broker;
    }

    private async Task WaitUntilReadyAsync()
    {
        using var adminClient = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        var deadline = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(3));
                if (metadata.Brokers.Count > 0)
                {
                    return;
                }
            }
            catch (Exception)
            {
                // Broker still starting up (KRaft formatting, controller election); retry.
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"Kafka test container '{_containerName}' did not become ready in time.");
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task RunDockerAsync(string[] args, string description)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to {description}: {stderr}{stdout}");
        }
    }

    private bool _disposed;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        try
        {
            await RunDockerAsync(["stop", "-t", "5", _containerName], $"stop Kafka test container '{_containerName}'");
        }
        catch
        {
            // Best-effort teardown — the container is --rm, so a stop failure here would
            // only leak a container if the daemon itself is in a bad state.
        }
    }
}
