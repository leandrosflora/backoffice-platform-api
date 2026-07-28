namespace DocumentIntelligence.Api.OpenAi;

public sealed class OpenAiOptions
{
    public string ApiKey { get; set; } = "";

    /// <summary>Must be a vision- and tool-calling-capable model (e.g. "gpt-4o").</summary>
    public string Model { get; set; } = "gpt-4o";

    public int MaxOutputTokens { get; set; } = 2048;
}
