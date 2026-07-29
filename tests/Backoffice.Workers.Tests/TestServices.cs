using Backoffice.Application.Abstractions;
using Backoffice.Application.Documents;
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
    private readonly string _documentStorageRoot;

    public FakeClock Clock { get; }

    public IServiceScopeFactory ScopeFactory => _serviceProvider.GetRequiredService<IServiceScopeFactory>();

    private TestServices(
        SqliteConnection connection,
        ServiceProvider serviceProvider,
        FakeClock clock,
        string documentStorageRoot)
    {
        _connection = connection;
        _serviceProvider = serviceProvider;
        Clock = clock;
        _documentStorageRoot = documentStorageRoot;
    }

    public static async Task<TestServices> CreateAsync(string kafkaBootstrapServers = "unused:9092")
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var documentStorageRoot = Path.Combine(
            Path.GetTempPath(), "backoffice-worker-tests", Guid.NewGuid().ToString("N"));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:BootstrapServers"] = kafkaBootstrapServers,
                ["Kafka:EventsTopic"] = "backoffice.events.v1",
                ["Kafka:DlqTopic"] = "backoffice.dlq.v1",
                ["Kafka:ConsumerGroup"] = "backoffice-workflow-v1",
                ["DocumentStorage:RootPath"] = documentStorageRoot,
                ["MalwareScan:Mode"] = "noop",
            })
            .Build();

        var clock = new FakeClock();
        var services = new ServiceCollection();
        services.AddDbContext<BackofficeDbContext>(options => options.UseSqlite(connection));
        services.AddInfrastructureCore(configuration);
        services.AddKafkaEventing(configuration);
        services.AddSingleton<IClock>(clock); // registered last so it wins over AddInfrastructureCore's SystemClock
        services.AddSingleton<IDocumentIntelligenceClient>(new WorkerFakeDocumentIntelligenceClient());

        var serviceProvider = services.BuildServiceProvider();

        using (var scope = serviceProvider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<BackofficeDbContext>().Database.EnsureCreatedAsync();
        }

        return new TestServices(connection, serviceProvider, clock, documentStorageRoot);
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
        if (Directory.Exists(_documentStorageRoot))
        {
            Directory.Delete(_documentStorageRoot, recursive: true);
        }
    }

    private sealed class WorkerFakeDocumentIntelligenceClient : IDocumentIntelligenceClient
    {
        public Task<DocumentAnalysisResult> AnalyzeAsync(
            byte[] fileContent,
            string fileName,
            string mediaType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DocumentAnalysisResult(
                "RECEIPT", 0.9, [], false, "Deterministic worker test result."));
    }
}
