using Backoffice.Application.Abstractions;
using Backoffice.Application.Approvals;
using Backoffice.Application.Audit;
using Backoffice.Application.Cases;
using Backoffice.Application.Documents;
using Backoffice.Application.Eventing;
using Backoffice.Application.Executions;
using Backoffice.Application.Identity;
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

        var storageOptions = new DocumentStorageOptions
        {
            RootPath = configuration["DocumentStorage:RootPath"] ?? ".local/document-storage",
            MaxUploadBytes = ParsePositiveLong(
                configuration["DocumentStorage:MaxUploadBytes"],
                DocumentStorageOptions.DefaultMaxUploadBytes,
                "DocumentStorage:MaxUploadBytes"),
        };
        var processingOptions = new DocumentProcessingOptions
        {
            Inline = bool.TryParse(configuration["DocumentProcessing:Inline"], out var inline) && inline,
        };
        var clamAvOptions = new ClamAvOptions
        {
            Host = configuration["MalwareScan:ClamAv:Host"] ?? "localhost",
            Port = ParsePositiveInt(configuration["MalwareScan:ClamAv:Port"], 3310, "MalwareScan:ClamAv:Port"),
            TimeoutSeconds = ParsePositiveInt(
                configuration["MalwareScan:ClamAv:TimeoutSeconds"], 30, "MalwareScan:ClamAv:TimeoutSeconds"),
        };
        var malwareScanMode = (configuration["MalwareScan:Mode"] ?? "clamav").ToLowerInvariant();
        if (malwareScanMode is not ("clamav" or "noop"))
        {
            throw new InvalidOperationException(
                $"Unsupported MalwareScan:Mode '{malwareScanMode}'. Expected 'clamav' or 'noop'.");
        }

        services.AddSingleton(storageOptions);
        services.AddSingleton(processingOptions);
        services.AddSingleton(clamAvOptions);
        services.AddSingleton<IDocumentStorage, FileSystemDocumentStorage>();
        services.AddScoped<NoOpMalwareScanAdapter>();
        services.AddScoped<ClamAvMalwareScanAdapter>();
        services.AddScoped<IMalwareScanAdapter>(serviceProvider =>
            malwareScanMode == "noop"
                ? serviceProvider.GetRequiredService<NoOpMalwareScanAdapter>()
                : serviceProvider.GetRequiredService<ClamAvMalwareScanAdapter>());
        services.AddHttpClient<IDocumentIntelligenceClient, HttpDocumentIntelligenceClient>(client =>
        {
            client.BaseAddress = new Uri(configuration["DocumentIntelligence:BaseUrl"] ?? "http://localhost:8090");
            client.Timeout = TimeSpan.FromSeconds(35);
        });
        services.AddScoped<RegisterDocumentHandler>();
        services.AddScoped<ProcessDocumentHandler>();
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
        // Default no-op registration so PolicyEnforcer resolves in every host (e.g.
        // Backoffice.Workers, which never actually calls EnforceAsync); the Api host
        // overrides this with a real HttpContext-backed accessor (registered after this
        // call wins, same "last registration wins" pattern used for test IClock overrides).
        services.AddScoped<ICallerIdentityAccessor, NullCallerIdentityAccessor>();

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

    private static int ParsePositiveInt(string? configuredValue, int fallback, string settingName)
    {
        if (configuredValue is null)
        {
            return fallback;
        }

        if (int.TryParse(configuredValue, out var value) && value > 0)
        {
            return value;
        }

        throw new InvalidOperationException($"{settingName} must be a positive integer.");
    }

    private static long ParsePositiveLong(string? configuredValue, long fallback, string settingName)
    {
        if (configuredValue is null)
        {
            return fallback;
        }

        if (long.TryParse(configuredValue, out var value) && value > 0)
        {
            return value;
        }

        throw new InvalidOperationException($"{settingName} must be a positive integer.");
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
