using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backoffice.Application.Policy;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Backoffice.Infrastructure.Policy;

/// <summary>
/// Calls the unmodified OPA Rego policy (policies/authorization.rego) over HTTP. A short
/// timeout plus a single quick retry absorbs transient blips without turning a genuinely
/// down PDP into a long hang — either way, any failure resolves to
/// <see cref="AuthorizationDecision.Unavailable"/> rather than throwing, so callers always
/// fail closed (spec: policy-authorization).
/// </summary>
public sealed class OpaPolicyDecisionClient : IPolicyDecisionClient
{
    private const string DecisionPath = "v1/data/intelligent_backoffice/authorization/decision";

    private readonly HttpClient _httpClient;
    private readonly ILogger<OpaPolicyDecisionClient> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public OpaPolicyDecisionClient(HttpClient httpClient, ILogger<OpaPolicyDecisionClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.FromMilliseconds(100),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>(),
            })
            .AddTimeout(TimeSpan.FromSeconds(5))
            .Build();
    }

    public async Task<AuthorizationDecision> EvaluateAsync(AuthorizationInput input, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _pipeline.ExecuteAsync(
                async token => await _httpClient.PostAsJsonAsync(DecisionPath, new OpaRequestEnvelope(input), token),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OPA returned non-success status {StatusCode} for action {Action}.", response.StatusCode, input.Action);
                return AuthorizationDecision.Unavailable($"opa-http-{(int)response.StatusCode}");
            }

            var envelope = await response.Content.ReadFromJsonAsync<OpaResponseEnvelope>(cancellationToken: cancellationToken);
            if (envelope?.Result is null)
            {
                return AuthorizationDecision.Unavailable("opa-empty-result");
            }

            return envelope.Result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "OPA policy decision point unavailable for action {Action}.", input.Action);
            return AuthorizationDecision.Unavailable("pdp-unreachable");
        }
    }

    private sealed record OpaRequestEnvelope([property: JsonPropertyName("input")] AuthorizationInput Input);

    private sealed record OpaResponseEnvelope([property: JsonPropertyName("result")] AuthorizationDecision? Result);
}
