namespace Backoffice.Domain.Evidence;

public enum EvidenceType
{
    ExtractedField,
    SystemFact,
    AnalystNote,
    PolicyReference,
    ModelFinding,
}

public enum EvidenceSourceType
{
    Document,
    System,
    Human,
    KnowledgeBase,
    Model,
}
