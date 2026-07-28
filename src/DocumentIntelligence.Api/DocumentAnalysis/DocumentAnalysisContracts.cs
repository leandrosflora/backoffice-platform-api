namespace DocumentIntelligence.Api.DocumentAnalysis;

public enum SupportedDocumentType
{
    Pdf,
    Jpeg,
    Png,
    Docx,
    Xlsx,
}

public sealed record ExtractedField(string Name, string Value, double Confidence);

/// <summary>
/// The service's own response contract — domain-agnostic (no disputes/case-management
/// vocabulary), so any future caller can consume it (spec: document-analysis-service).
/// </summary>
public sealed record DocumentAnalysisResult(
    string DocumentType,
    double Confidence,
    IReadOnlyList<ExtractedField> ExtractedFields,
    bool Abstained,
    string Rationale);

public sealed class UnsupportedDocumentTypeException(string mediaType) : Exception($"Unsupported document media type: {mediaType}");
