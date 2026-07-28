using DocumentIntelligence.Api.DocumentAnalysis;
using DocumentIntelligence.Api.OpenAi;
using OpenAI;
using OpenAI.Chat;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection("OpenAI"));
builder.Services.Configure<DocumentAnalysisOptions>(builder.Configuration.GetSection("DocumentAnalysis"));

// The OpenAI SDK's ClientPipeline already retries transient failures (5xx/429) internally, so
// no separate Polly wrapper is needed here (unlike the hand-rolled Anthropic client this
// replaced — see design.md's revision note on switching providers).
builder.Services.AddSingleton<ChatClient>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenAiOptions>>().Value;
    return new OpenAIClient(options.ApiKey).GetChatClient(options.Model);
});

builder.Services.AddScoped<DocumentAnalysisService>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/v1/documents/analyze", async (HttpRequest request, DocumentAnalysisService service, CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { detail = "Expected multipart/form-data with a 'file' field." });
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files["file"];
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { detail = "Missing 'file' field." });
    }

    SupportedDocumentType documentType;
    try
    {
        documentType = ResolveDocumentType(file.FileName, file.ContentType);
    }
    catch (UnsupportedDocumentTypeException exception)
    {
        return Results.BadRequest(new { detail = exception.Message });
    }

    await using var stream = file.OpenReadStream();
    var result = await service.AnalyzeAsync(stream, documentType, cancellationToken);
    return Results.Ok(result);
});

app.Run();

static SupportedDocumentType ResolveDocumentType(string fileName, string contentType)
{
    var extension = Path.GetExtension(fileName).ToLowerInvariant();
    return (extension, contentType.ToLowerInvariant()) switch
    {
        (".pdf", _) or (_, "application/pdf") => SupportedDocumentType.Pdf,
        (".jpg", _) or (".jpeg", _) or (_, "image/jpeg") => SupportedDocumentType.Jpeg,
        (".png", _) or (_, "image/png") => SupportedDocumentType.Png,
        (".docx", _) or (_, "application/vnd.openxmlformats-officedocument.wordprocessingml.document") => SupportedDocumentType.Docx,
        (".xlsx", _) or (_, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet") => SupportedDocumentType.Xlsx,
        _ => throw new UnsupportedDocumentTypeException($"{fileName} ({contentType})"),
    };
}

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program;
