using Backoffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backoffice.Contracts.Tests;

public sealed class RuntimeOpenApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
            services.AddDbContext<BackofficeDbContext>(options =>
                options.UseInMemoryDatabase($"runtime-openapi-{Guid.NewGuid()}")));
    }
}
