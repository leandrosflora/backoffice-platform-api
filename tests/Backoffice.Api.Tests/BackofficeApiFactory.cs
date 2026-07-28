using Backoffice.Application.Documents;
using Backoffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Backoffice.Api.Tests;

/// <summary>
/// Test host running under the "Testing" environment, where Program.cs skips Npgsql
/// registration entirely and this factory registers a real (if transient) relational
/// database instead: SQLite over an open in-memory connection. The EF Core InMemory
/// provider was tried first but does not reliably handle "update parent scalar + insert
/// child" across separate DbContext instances pointed at the same store (a documented
/// limitation, since it isn't a real relational engine) — SQLite in-memory mode avoids
/// that entirely while still requiring no external database for tests.
///
/// Also starts a real standalone OPA server process running the unmodified
/// policies/authorization.rego, so every test in this project exercises the actual policy
/// decision point rather than a stub (spec: policy-authorization). IAsyncLifetime is used
/// (not the constructor) because xUnit's IClassFixture activation requires a single public
/// parameterless constructor and cannot await async startup there.
/// </summary>
public sealed class BackofficeApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private OpaTestServer? _opaServer;

    /// <summary>
    /// Optional service overrides (e.g. a fake IClock) applied after the DbContext is
    /// registered. Must be set before the first CreateClient() call, since the host builds
    /// lazily on first use — xUnit's IClassFixture requires a single public parameterless
    /// constructor, so this is a settable property rather than a constructor parameter.
    /// </summary>
    public Action<IServiceCollection>? ConfigureTestServices { get; set; }

    /// <summary>The real OPA test server's base URL, available once InitializeAsync has run.</summary>
    public string OpaBaseUrl => _opaServer?.BaseUrl
        ?? throw new InvalidOperationException("OPA test server has not started yet.");

    public BackofficeApiFactory()
    {
        // Required: a SQLite ":memory:" database only exists while its connection is open,
        // and EnsureCreated() below silently no-ops against a closed one.
        _connection.Open();
    }

    public async Task InitializeAsync()
    {
        _opaServer = await OpaTestServer.StartAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Opa:BaseUrl", OpaBaseUrl);

        builder.ConfigureServices(services =>
        {
            services.AddDbContext<BackofficeDbContext>(options => options.UseSqlite(_connection));

            // Replaces the real HTTP-based client (which would otherwise call the paid
            // OpenAI-backed document-analysis service) with a deterministic, free,
            // filename-keyword fake — see FakeDocumentIntelligenceClient's own doc comment.
            services.AddScoped<IDocumentIntelligenceClient, FakeDocumentIntelligenceClient>();

            using var scope = services.BuildServiceProvider().CreateScope();
            scope.ServiceProvider.GetRequiredService<BackofficeDbContext>().Database.EnsureCreated();

            // Applied last so a test-supplied override (e.g. a fake IClock) wins.
            ConfigureTestServices?.Invoke(services);
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        // Disposing _opaServer happens in Dispose(bool) below, which base.DisposeAsync()
        // triggers — do not also dispose it here, or the process gets torn down twice.
        await base.DisposeAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
            _opaServer?.Dispose();
        }
    }
}
