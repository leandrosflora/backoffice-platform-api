using Backoffice.Application.Documents;

namespace Backoffice.Infrastructure.Documents;

/// <summary>
/// Deterministic, filename-keyword-based stand-in for <see cref="HttpDocumentIntelligenceClient"/>,
/// selected via <c>DocumentIntelligence:Mode=Fake</c> in <see cref="DependencyInjection.AddInfrastructureCore"/>
/// (default remains the real HTTP client). Exists for local/CI environments — such as
/// `intelligent-backoffice-frontend/e2e/docker-compose.yml` — that have no reachable
/// document-analysis service and no OpenAI API key, so document registration would otherwise
/// always abstain. Mirrors the exact keyword logic the old (removed)
/// `Backoffice.Domain.Documents.DocumentClassifier` used.
/// </summary>
public sealed class FakeDocumentIntelligenceClient : IDocumentIntelligenceClient
{
    private static readonly Dictionary<string, string[]> Keywords = new()
    {
        ["RECEIPT"] = ["receipt", "recibo", "comprovante-compra"],
        ["STATEMENT"] = ["statement", "extrato"],
        ["TRANSACTION_PROOF"] = ["transaction", "transacao", "comprovante-transacao", "pix", "transferencia"],
        ["IDENTITY_PROOF"] = ["identity", "identidade", "rg", "cpf-doc"],
    };

    public Task<DocumentAnalysisResult> AnalyzeAsync(
        byte[] fileContent, string fileName, string mediaType, CancellationToken cancellationToken = default)
    {
        var normalized = fileName.ToLowerInvariant();
        var match = Keywords.FirstOrDefault(kvp => kvp.Value.Any(normalized.Contains));

        if (match.Key is null)
        {
            return Task.FromResult(DocumentAnalysisResult.Abstain("no-filename-keyword-match"));
        }

        return Task.FromResult(new DocumentAnalysisResult(
            DocumentType: match.Key,
            Confidence: 0.9,
            ExtractedFields: [],
            Abstained: false,
            Rationale: $"Fake match on filename keyword for {match.Key}."));
    }
}
