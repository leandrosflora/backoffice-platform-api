using Backoffice.Infrastructure;
using Backoffice.Infrastructure.Observability;
using Backoffice.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddKafkaEventing(builder.Configuration);
builder.Services.AddObservability(builder.Configuration, serviceName: "backoffice-workers", isHttpHost: false);

builder.Services.AddHostedService<OutboxDispatcherWorker>();
builder.Services.AddHostedService<WorkflowConsumerWorker>();
builder.Services.AddHostedService<TimerFiringWorker>();

var host = builder.Build();
host.Run();
