using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace DocumentIntelligence.Api.DocumentAnalysis;

public sealed class DocumentAnalysisService(ChatClient chatClient, IOptions<DocumentAnalysisOptions> options)
{
    private readonly DocumentAnalysisOptions _options = options.Value;

    public async Task<DocumentAnalysisResult> AnalyzeAsync(
        Stream documentStream, SupportedDocumentType documentType, CancellationToken cancellationToken)
    {
        var userContentParts = await BuildUserContentAsync(documentStream, documentType, cancellationToken);

        var chatOptions = new ChatCompletionOptions
        {
            ToolChoice = ChatToolChoice.CreateFunctionChoice(DocumentAnalysisPrompts.ToolName),
        };
        chatOptions.Tools.Add(ChatTool.CreateFunctionTool(
            DocumentAnalysisPrompts.ToolName,
            $"Records the result of the analysis by calling {DocumentAnalysisPrompts.ToolName} exactly once with the required fields.",
            BinaryData.FromString(DocumentAnalysisPrompts.ToolInputSchema().ToJsonString())));

        ChatMessage[] messages =
        [
            new SystemChatMessage(DocumentAnalysisPrompts.SystemPrompt),
            new UserChatMessage(userContentParts),
        ];

        var completion = await chatClient.CompleteChatAsync(messages, chatOptions, cancellationToken);
        var toolCall = completion.Value.ToolCalls.FirstOrDefault(call => call.FunctionName == DocumentAnalysisPrompts.ToolName)
            ?? throw new InvalidOperationException($"Model response contained no '{DocumentAnalysisPrompts.ToolName}' tool call.");

        return ParseToolArguments(toolCall.FunctionArguments.ToString(), _options.ConfidenceFloor);
    }

    /// <summary>
    /// Pure parsing/abstention logic, decoupled from the network call so it can be unit
    /// tested against recorded real API response fixtures without needing to mock the
    /// (sealed) OpenAI SDK <see cref="ChatClient"/> (spec: document-analysis-service, task 4.1).
    /// </summary>
    public static DocumentAnalysisResult ParseToolArguments(string toolArgumentsJson, double confidenceFloor)
    {
        using var argumentsDocument = JsonDocument.Parse(toolArgumentsJson);
        var arguments = argumentsDocument.RootElement;

        var modelConfidence = arguments.GetProperty("confidence").GetDouble();
        var modelAbstained = arguments.GetProperty("abstained").GetBoolean();
        var extractedFields = arguments.GetProperty("extractedFields").EnumerateArray()
            .Select(field => new ExtractedField(
                field.GetProperty("name").GetString() ?? "",
                field.GetProperty("value").GetString() ?? "",
                field.GetProperty("confidence").GetDouble()))
            .ToList();

        // The confidence floor is enforced here regardless of what the model itself reported
        // for `abstained` — design.md's "Abstention is enforced in code, not just trusted from
        // the model" decision.
        var abstained = modelAbstained || modelConfidence < confidenceFloor;

        return new DocumentAnalysisResult(
            DocumentType: abstained ? "OTHER" : arguments.GetProperty("documentType").GetString() ?? "OTHER",
            Confidence: modelConfidence,
            ExtractedFields: abstained ? [] : extractedFields,
            Abstained: abstained,
            Rationale: arguments.GetProperty("rationale").GetString() ?? "");
    }

    private static async Task<List<ChatMessageContentPart>> BuildUserContentAsync(
        Stream documentStream, SupportedDocumentType documentType, CancellationToken cancellationToken)
    {
        const string instruction = "Analyze the attached document and call record_document_analysis with your findings.";

        switch (documentType)
        {
            case SupportedDocumentType.Pdf:
                // CreateFilePart is marked OPENAI001 ("for evaluation purposes") in the
                // current OpenAI SDK — native PDF input is a newer API surface still subject
                // to change upstream, but it's the only way to send a PDF natively rather than
                // rasterizing pages ourselves.
#pragma warning disable OPENAI001
                var pdfPart = ChatMessageContentPart.CreateFilePart(await ReadBinaryDataAsync(documentStream, cancellationToken), "application/pdf", "document.pdf");
#pragma warning restore OPENAI001
                return [pdfPart, ChatMessageContentPart.CreateTextPart(instruction)];

            case SupportedDocumentType.Jpeg or SupportedDocumentType.Png:
                var mediaType = documentType == SupportedDocumentType.Jpeg ? "image/jpeg" : "image/png";
                return
                [
                    ChatMessageContentPart.CreateImagePart(await ReadBinaryDataAsync(documentStream, cancellationToken), mediaType),
                    ChatMessageContentPart.CreateTextPart(instruction),
                ];

            case SupportedDocumentType.Docx:
                var docxText = DocumentTextExtractor.ExtractDocxText(documentStream);
                return [ChatMessageContentPart.CreateTextPart($"{instruction}\n\n<document_content>\n{docxText}\n</document_content>")];

            case SupportedDocumentType.Xlsx:
                var xlsxText = DocumentTextExtractor.ExtractXlsxText(documentStream);
                return [ChatMessageContentPart.CreateTextPart($"{instruction}\n\n<document_content>\n{xlsxText}\n</document_content>")];

            default:
                throw new UnsupportedDocumentTypeException(documentType.ToString());
        }
    }

    private static async Task<BinaryData> ReadBinaryDataAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        return BinaryData.FromBytes(memoryStream.ToArray());
    }
}
