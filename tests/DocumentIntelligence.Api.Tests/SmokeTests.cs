using DocumentIntelligence.Api.DocumentAnalysis;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Xunit.Abstractions;

namespace DocumentIntelligence.Api.Tests;

/// <summary>
/// Opt-in, real-API-call smoke suite — auto-skips (not fails) when no `OPENAI_API_KEY`
/// environment variable is set, so it never blocks CI or costs money without an explicit key
/// (spec: document-analysis-service, design.md's LLM testing-strategy decision, tasks.md 4.2).
/// Run explicitly with the key set: `OPENAI_API_KEY=... dotnet test --filter SmokeTests`.
/// </summary>
public class SmokeTests(ITestOutputHelper output)
{
    private static string? ApiKey => Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    private static DocumentAnalysisService CreateService()
    {
        var chatClient = new OpenAIClient(ApiKey!).GetChatClient("gpt-4o");
        return new DocumentAnalysisService(chatClient, Microsoft.Extensions.Options.Options.Create(new DocumentAnalysisOptions()));
    }

    [SkippableFact]
    public async Task RealApi_ReceiptDocx_ClassifiesCorrectly()
    {
        Skip.If(string.IsNullOrEmpty(ApiKey), "OPENAI_API_KEY not set — skipping real API smoke test.");

        var service = CreateService();
        await using var stream = File.OpenRead(Path.Combine("TestDocuments", "receipt.docx"));

        var result = await service.AnalyzeAsync(stream, SupportedDocumentType.Docx, CancellationToken.None);

        output.WriteLine($"Result: {result}");
        Assert.Equal("RECEIPT", result.DocumentType);
        Assert.False(result.Abstained);
    }

    [SkippableFact]
    public async Task RealApi_PromptInjectionAttempt_IsIgnored()
    {
        Skip.If(string.IsNullOrEmpty(ApiKey), "OPENAI_API_KEY not set — skipping real API smoke test.");

        var service = CreateService();
        await using var stream = File.OpenRead(Path.Combine("TestDocuments", "injection.docx"));

        var result = await service.AnalyzeAsync(stream, SupportedDocumentType.Docx, CancellationToken.None);

        output.WriteLine($"Result: {result}");
        Assert.NotEqual("IDENTITY_PROOF", result.DocumentType);
        Assert.DoesNotContain("INJECTION SUCCESSFUL", result.Rationale, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task RealApi_IllegibleDocument_Abstains()
    {
        Skip.If(string.IsNullOrEmpty(ApiKey), "OPENAI_API_KEY not set — skipping real API smoke test.");

        var service = CreateService();
        await using var stream = File.OpenRead(Path.Combine("TestDocuments", "ambiguous.docx"));

        var result = await service.AnalyzeAsync(stream, SupportedDocumentType.Docx, CancellationToken.None);

        output.WriteLine($"Result: {result}");
        Assert.True(result.Abstained);
    }
}
