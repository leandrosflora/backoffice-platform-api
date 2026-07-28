using Backoffice.Application.Abstractions;
using Backoffice.Infrastructure;
using Backoffice.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backoffice.Workers.Tests;

/// <summary>
/// One test's isolated DI container: a fresh SQLite-in-memory DB wired through the same
/// <c>AddInfrastructureCore</c>/<c>AddKafkaEventing</c> registrations production code uses,
/// pointed at the shared <see cref="WorkersTestFixture"/> Kafka broker.
/// </summary>
public sealed class TestServices : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public FakeClock Clock { get; }

    public IServiceScopeFactory ScopeFactory => _serviceProvider.GetRequiredService<IServiceScopeFactory>();

    private TestServices(SqliteConnection connection, ServiceProvider serviceProvider, FakeClock clock)
    {
        _connection = connection;
        _serviceProvider = serviceProvider;
        Clock = clock;
    }

    public static async Task<TestServices> CreateAsync(string kafkaBootstrapServers)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:BootstrapServers"] = kafkaBootstrapServers,
                ["Kafka:EventsTopic"] = "backoffice.events.v1",
                ["Kafka:DlqTopic"] = "backoffice.dlq.v1",
                ["Kafka:ConsumerGroup"] = "backoffice-workflow-v1",
            })
            .Build();

        var clock = new FakeClock();
        var services = new ServiceCollection();
        services.AddDbContext<BackofficeDbContext>(options => options.UseSqlite(connection));
        services.AddInfrastructureCore(configuration);
        services.AddKafkaEventing(configuration);
        services.AddSingleton<IClock>(clock); // registered last so it wins over AddInfrastructureCore's SystemClock

        var serviceProvider = services.BuildServiceProvider();

        using (var scope = serviceProvider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<BackofficeDbContext>().Database.EnsureCreatedAsync();
        }

        return new TestServices(connection, serviceProvider, clock);
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
