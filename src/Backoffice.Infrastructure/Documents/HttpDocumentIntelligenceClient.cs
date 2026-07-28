using System.Net.Http.Headers;
using System.Text.Json;
using Backoffice.Application.Documents;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Backoffice.Infrastructure.Documents;

/// <summary>
/// Calls the standalone `document-analysis-service` (`DocumentIntelligence.Api`) over HTTP —
/// mirrors `OpaPolicyDecisionClient`'s resilience shape (short timeout, one quick retry,
/// never throws) but resolves failures to <see cref="DocumentAnalysisResult.Abstain"/> rather
/// than a fail-closed sentinel, since document classification isn't an authorization boundary
/// (spec: document-intelligence, "Document-analysis service unavailability degrades to
/// abstention").
/// </summary>
public sealed class HttpDocumentIntelligenceClient : IDocumentIntelligenceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpDocumentIntelligenceClient> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public HttpDocumentIntelligenceClient(HttpClient httpClient, ILogger<HttpDocumentIntelligenceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.FromMilliseconds(200),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>(),
            })
            .AddTimeout(TimeSpan.FromSeconds(30)) // real LLM calls are slower than OPA's ~5s budget
            .Build();
    }

    public async Task<DocumentAnalysisResult> AnalyzeAsync(
        byte[] fileContent, string fileName, string mediaType, CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            using var fileStreamContent = new ByteArrayContent(fileContent);
            fileStreamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
            content.Add(fileStreamContent, "file", fileName);

            var response = await _pipeline.ExecuteAsync(
                async token => await _httpClient.PostAsync("/v1/documents/analyze", content, token),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Document-analysis service returned non-success status {StatusCode}.", response.StatusCode);
                return DocumentAnalysisResult.Abstain($"document-intelligence-http-{(int)response.StatusCode}");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var fields = root.GetProperty("extractedFields").EnumerateArray()
                .Select(field => new ExtractedFieldDto(
                    field.GetProperty("name").GetString() ?? "",
                    field.GetProperty("value").GetString() ?? "",
                    field.GetProperty("confidence").GetDouble()))
                .ToList();

            return new DocumentAnalysisResult(
                DocumentType: root.GetProperty("documentType").GetString() ?? "OTHER",
                Confidence: root.GetProperty("confidence").GetDouble(),
                ExtractedFields: fields,
                Abstained: root.GetProperty("abstained").GetBoolean(),
                Rationale: root.GetProperty("rationale").GetString() ?? "");
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Document-analysis service unreachable.");
            return DocumentAnalysisResult.Abstain("document-intelligence-unreachable");
        }
    }
}
