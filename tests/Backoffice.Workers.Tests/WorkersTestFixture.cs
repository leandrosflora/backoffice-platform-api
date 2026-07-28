namespace Backoffice.Workers.Tests;

/// <summary>
/// Shares only the expensive-to-start Kafka broker container across every test in the class.
/// Each test builds its own isolated <see cref="TestServices"/> (fresh SQLite-in-memory DB)
/// against this same broker — <c>ClaimAsync</c> is deliberately tenant-agnostic (it claims
/// across every tenant, matching the real dispatcher), so tests sharing one database would
/// otherwise see each other's outbox/timer rows.
/// </summary>
public sealed class WorkersTestFixture : IAsyncLifetime
{
    private KafkaTestBroker? _kafkaBroker;

    public string KafkaBootstrapServers => _kafkaBroker?.BootstrapServers
        ?? throw new InvalidOperationException("Kafka test broker not started.");

    public async Task InitializeAsync()
    {
        _kafkaBroker = await KafkaTestBroker.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_kafkaBroker is not null)
        {
            await _kafkaBroker.DisposeAsync();
        }
    }
}
