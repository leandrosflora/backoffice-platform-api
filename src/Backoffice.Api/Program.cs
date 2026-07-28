using System.Text.Json.Serialization;
using Backoffice.Api;
using Backoffice.Api.Approvals;
using Backoffice.Api.Cases;
using Backoffice.Api.Documents;
using Backoffice.Api.Executions;
using Backoffice.Api.Investigations;
using Backoffice.Api.Recommendations;
using Backoffice.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Test hosts (WebApplicationFactory) set ASPNETCORE_ENVIRONMENT=Testing and register
// their own DbContext provider (e.g. EF Core InMemory) via AddInfrastructureCore(),
// so Npgsql is never added to a service collection a test then has to un-register.
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddInfrastructure(builder.Configuration);
}
else
{
    builder.Services.AddInfrastructureCore(builder.Configuration);
}
builder.Services.AddExceptionHandler<BackofficeExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(ScreamingSnakeCaseNamingPolicy.Instance));
});

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapCasesEndpoints();
app.MapDocumentsEndpoints();
app.MapInvestigationsEndpoints();
app.MapRecommendationsEndpoints();
app.MapApprovalsEndpoints();
app.MapExecutionsEndpoints();

app.Run();

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program;
