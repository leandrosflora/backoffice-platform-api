using Backoffice.Infrastructure;
using Backoffice.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddKafkaEventing(builder.Configuration);

builder.Services.AddHostedService<OutboxDispatcherWorker>();
builder.Services.AddHostedService<WorkflowConsumerWorker>();
builder.Services.AddHostedService<TimerFiringWorker>();

var host = builder.Build();
host.Run();
