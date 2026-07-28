namespace DocumentIntelligence.Api.DocumentAnalysis;

public sealed class DocumentAnalysisOptions
{
    /// <summary>
    /// Minimum confidence (on top of, not instead of, the model's own self-reported
    /// confidence) required to accept a non-abstained result — enforced in code, never just
    /// trusted from the model (design.md's "Abstention is enforced in code" decision).
    /// </summary>
    public double ConfidenceFloor { get; set; } = 0.7;
}
