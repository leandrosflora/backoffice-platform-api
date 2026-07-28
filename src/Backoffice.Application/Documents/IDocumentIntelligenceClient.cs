namespace Backoffice.Application.Documents;

/// <summary>
/// Client for the standalone `document-analysis-service` (spec: document-analysis-service),
/// replacing the old deterministic `Backoffice.Domain.Documents.DocumentClassifier`. Never
/// throws — an unreachable/erroring service resolves to <see cref="DocumentAnalysisResult.Abstain"/>,
/// matching the "unavailability degrades to abstention, not a hard failure" decision in this
/// change's design.md, mirroring how `IPolicyDecisionClient` always resolves rather than throws.
/// </summary>
public interface IDocumentIntelligenceClient
{
    Task<DocumentAnalysisResult> AnalyzeAsync(
        byte[] fileContent, string fileName, string mediaType, CancellationToken cancellationToken = default);
}

public sealed record ExtractedFieldDto(string Name, string Value, double Confidence);

public sealed record DocumentAnalysisResult(
    string DocumentType,
    double Confidence,
    IReadOnlyList<ExtractedFieldDto> ExtractedFields,
    bool Abstained,
    string Rationale)
{
    public static DocumentAnalysisResult Abstain(string reason) => new("OTHER", 0.0, [], true, reason);
}
