namespace Backoffice.Evals;

public sealed class ThresholdsConfig
{
    public int Version { get; set; }
    public double OverallMinScore { get; set; }
    public Dictionary<string, TaskThreshold> Tasks { get; set; } = new();
    public Dictionary<string, GuardrailThreshold> Guardrails { get; set; } = new();
}

public sealed class TaskThreshold
{
    public double MinScore { get; set; }
}

public sealed class GuardrailThreshold
{
    public double Min { get; set; }
}
