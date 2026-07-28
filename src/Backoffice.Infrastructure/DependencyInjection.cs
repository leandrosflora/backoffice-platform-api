using Backoffice.Application.Abstractions;
using Backoffice.Application.Approvals;
using Backoffice.Application.Audit;
using Backoffice.Application.Cases;
using Backoffice.Application.Documents;
using Backoffice.Application.Eventing;
using Backoffice.Application.Executions;
using Backoffice.Application.Investigations;
using Backoffice.Application.Policy;
using Backoffice.Application.Recommendations;
using Backoffice.Infrastructure.Approvals;
using Backoffice.Infrastructure.Audit;
using Backoffice.Infrastructure.Documents;
using Backoffice.Infrastructure.Eventing;
using Backoffice.Infrastructure.Executions;
using Backoffice.Infrastructure.Investigations;
using Backoffice.Infrastructure.Persistence;
using Backoffice.Infrastructure.Policy;
using Backoffice.Infrastructure.Recommendations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backoffice.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the PostgreSQL-backed persistence stack. Test hosts should register
    /// <see cref="BackofficeDbContext"/> with a different provider (e.g. EF Core InMemory)
    /// instead of calling this method.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<BackofficeDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Backoffice")));

        return services.AddInfrastructureCore(configuration);
    }

    /// <summary>
    /// Registers the persistence-agnostic bindings (repositories, unit of work, clock)
    /// shared by production and test hosts, regardless of which DbContext provider is used.
    /// </summary>
    public static IServiceCollection AddInfrastructureCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICaseRepository, CaseRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<CreateCaseHandler>();
        services.AddScoped<GetCaseHandler>();
        services.AddScoped<ListCasesHandler>();
        services.AddScoped<CancelCaseHandler>();
        services.AddScoped<GetCaseTimelineHandler>();

        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IEvidenceRepository, EvidenceRepository>();
        services.AddScoped<IMalwareScanAdapter, NoOpMalwareScanAdapter>();
        services.AddScoped<RegisterDocumentHandler>();
        services.AddScoped<GetDocumentHandler>();
        services.AddScoped<ListEvidenceHandler>();

        services.AddScoped<IInvestigationRepository, InvestigationRepository>();
        services.AddScoped<StartInvestigationHandler>();
        services.AddScoped<IRecommendationRepository, RecommendationRepository>();
        services.AddScoped<CreateRecommendationHandler>();

        services.AddScoped<IApprovalRepository, ApprovalRepository>();
        services.AddScoped<DecideApprovalHandler>();

        services.AddScoped<IExecutionRepository, ExecutionRepository>();
        services.AddScoped<IIdempotencyRecordRepository, IdempotencyRecordRepository>();
        services.AddScoped<IExecutionGateway, MockExecutionGateway>();
        services.AddScoped<RequestExecutionHandler>();
        services.AddScoped<ResolveReconciliationHandler>();
        services.AddScoped<GetExecutionHandler>();
        services.AddScoped<ListExecutionsHandler>();

        services.AddHttpClient<IPolicyDecisionClient, OpaPolicyDecisionClient>(client =>
        {
            client.BaseAddress = new Uri(configuration["Opa:BaseUrl"] ?? "http://localhost:8181");
        });
        services.AddScoped<PolicyEnforcer>();

        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IInboxRepository, InboxRepository>();
        services.AddScoped<ITimerRepository, TimerRepository>();
        services.AddScoped<IDeadLetterRepository, DeadLetterRepository>();
        services.AddScoped<IReplayAuditRepository, ReplayAuditRepository>();
        services.AddScoped<ScheduleTimerHandler>();
        services.AddScoped<ListOutboxHandler>();
        services.AddScoped<ListDeadLettersHandler>();
        services.AddScoped<ListTimersHandler>();
        services.AddScoped<ReplayDeadLetterHandler>();

        services.AddScoped<IAuditRepository, AuditRepository>();

        return services;
    }

    /// <summary>
    /// Kafka client construction, used only by the Backoffice.Workers host — the Api host
    /// never produces/consumes directly, it only reads the outbox/timer/dead-letter tables
    /// through the repositories registered above.
    /// </summary>
    public static IServiceCollection AddKafkaEventing(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<KafkaSettings>(configuration.GetSection("Kafka"));
        services.AddSingleton<IKafkaClientFactory, KafkaClientFactory>();

        return services;
    }
}
