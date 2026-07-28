using System.Text.Json.Serialization;
using Backoffice.Api;
using Backoffice.Api.Approvals;
using Backoffice.Api.Cases;
using Backoffice.Api.Documents;
using Backoffice.Api.Executions;
using Backoffice.Api.Identity;
using Backoffice.Api.Investigations;
using Backoffice.Api.Observability;
using Backoffice.Api.Operations;
using Backoffice.Api.Recommendations;
using Backoffice.Application.Identity;
using Backoffice.Infrastructure;
using Backoffice.Infrastructure.Identity;
using Backoffice.Infrastructure.Observability;
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

builder.Services.AddObservability(builder.Configuration, serviceName: "backoffice-api", isHttpHost: true);
builder.Services.AddEventingGauges();

// Secure/jwt profile (spec: identity-security). IJwtIdentityValidator is registered as a
// singleton but constructed lazily — the middleware only resolves it inside the jwt-mode
// branch, so a default "headers" profile app never needs Identity:PublicKeyPath configured.
builder.Services.Configure<IdentityOptions>(builder.Configuration.GetSection("Identity"));
builder.Services.AddSingleton<IJwtIdentityValidator, JwtIdentityValidator>();
builder.Services.AddHttpContextAccessor();
// Overrides AddInfrastructureCore's NullCallerIdentityAccessor — registered last, so it wins.
builder.Services.AddScoped<ICallerIdentityAccessor, HttpContextCallerIdentityAccessor>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<JwtIdentityMiddleware>();
app.UseMiddleware<HttpMetricsMiddleware>();
app.MapPrometheusScrapingEndpoint();
app.Services.GetRequiredService<EventingGaugeRegistrar>(); // eagerly registers the outbox/dead-letter/timer gauges

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapCasesEndpoints();
app.MapDocumentsEndpoints();
app.MapInvestigationsEndpoints();
app.MapRecommendationsEndpoints();
app.MapApprovalsEndpoints();
app.MapExecutionsEndpoints();
app.MapOperationsEndpoints();

app.Run();

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program;
