using Backoffice.Application.Documents;

namespace Backoffice.Api.Tests;

/// <summary>
/// Deterministic, filename-keyword-based stand-in for the real `document-analysis-service`
/// HTTP client, registered in <see cref="BackofficeApiFactory"/> so this whole test project
/// stays fast, free, and deterministic — the real AI/OCR integration is exercised separately
/// in `DocumentIntelligence.Api.Tests` (recorded fixtures + an opt-in real-API smoke suite),
/// not by making a paid network call on every test in this project that merely needs a
/// document registered as setup. Mirrors the exact keyword logic the old (now removed)
/// `Backoffice.Domain.Documents.DocumentClassifier` used, so every existing test's
/// filename-driven expectations (e.g. "receipt-....pdf" classifies, "file-....pdf" abstains)
/// keep working unchanged.
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
