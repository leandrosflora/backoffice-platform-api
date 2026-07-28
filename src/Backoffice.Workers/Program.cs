using Backoffice.Infrastructure;
using Backoffice.Infrastructure.Observability;
using Backoffice.Workers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

// A minimal Kestrel host, not a plain generic Host — Kubernetes/compose liveness probes need
// something to hit, and workers have no other listening port. Metrics/ASP.NET Core tracing
// stay off (AddObservability's isHttpHost: false below), so this is exclusively a health
// endpoint, not a second copy of the Api's public surface (spec: platform-deployment, task
// 12.3's readiness/liveness probes for worker deployables).
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddKafkaEventing(builder.Configuration);
builder.Services.AddObservability(builder.Configuration, serviceName: "backoffice-workers", isHttpHost: false);

// Worker:Role selects which hosted service(s) this process runs. "all" (the default) runs
// every worker in one process, for the single-container runtime/secure compose profiles;
// the distributed profile and Kubernetes instead run three containers from this same image,
// each pinned to one role, so outbox dispatch/workflow consumption/timer firing scale and
// fail independently (spec: platform-deployment, task 12.1).
var role = builder.Configuration["Worker:Role"] ?? "all";
switch (role)
{
    case "outbox-dispatcher":
        builder.Services.AddHostedService<OutboxDispatcherWorker>();
        break;
    case "workflow-consumer":
        builder.Services.AddHostedService<WorkflowConsumerWorker>();
        break;
    case "timer-firing":
        builder.Services.AddHostedService<TimerFiringWorker>();
        break;
    case "all":
        builder.Services.AddHostedService<OutboxDispatcherWorker>();
        builder.Services.AddHostedService<WorkflowConsumerWorker>();
        builder.Services.AddHostedService<TimerFiringWorker>();
        break;
    default:
        throw new InvalidOperationException(
            $"Unknown Worker:Role '{role}'. Expected one of: all, outbox-dispatcher, workflow-consumer, timer-firing.");
}

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();
