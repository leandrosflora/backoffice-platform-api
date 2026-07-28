using Backoffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backoffice.Api.Tests;

/// <summary>
/// Test host running under the "Testing" environment, where Program.cs skips Npgsql
/// registration entirely and this factory registers a real (if transient) relational
/// database instead: SQLite over an open in-memory connection. The EF Core InMemory
/// provider was tried first but does not reliably handle "update parent scalar + insert
/// child" across separate DbContext instances pointed at the same store (a documented
/// limitation, since it isn't a real relational engine) — SQLite in-memory mode avoids
/// that entirely while still requiring no external database for tests.
/// </summary>
public sealed class BackofficeApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    /// <summary>
    /// Optional service overrides (e.g. a fake IClock) applied after the DbContext is
    /// registered. Must be set before the first CreateClient() call, since the host builds
    /// lazily on first use — xUnit's IClassFixture requires a single public parameterless
    /// constructor, so this is a settable property rather than a constructor parameter.
    /// </summary>
    public Action<IServiceCollection>? ConfigureTestServices { get; set; }

    public BackofficeApiFactory()
    {
        // Required: a SQLite ":memory:" database only exists while its connection is open,
        // and EnsureCreated() below silently no-ops against a closed one.
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.AddDbContext<BackofficeDbContext>(options => options.UseSqlite(_connection));

            using var scope = services.BuildServiceProvider().CreateScope();
            scope.ServiceProvider.GetRequiredService<BackofficeDbContext>().Database.EnsureCreated();

            // Applied last so a test-supplied override (e.g. a fake IClock) wins.
            ConfigureTestServices?.Invoke(services);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
