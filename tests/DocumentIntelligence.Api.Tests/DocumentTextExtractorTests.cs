using DocumentIntelligence.Api.DocumentAnalysis;

namespace DocumentIntelligence.Api.Tests;

public class DocumentTextExtractorTests
{
    private static Stream OpenTestDocument(string name) => File.OpenRead(Path.Combine("TestDocuments", name));

    [Fact]
    public void ExtractDocxText_ReturnsParagraphText()
    {
        using var stream = OpenTestDocument("receipt.docx");

        var text = DocumentTextExtractor.ExtractDocxText(stream);

        Assert.Contains("LOJA EXEMPLO LTDA", text);
        Assert.Contains("R$ 120,00", text);
        Assert.Contains("987654321", text);
    }

    [Fact]
    public void ExtractXlsxText_ReturnsRowColumnTable()
    {
        using var stream = OpenTestDocument("statement.xlsx");

        var text = DocumentTextExtractor.ExtractXlsxText(stream);

        Assert.Contains("Transferencia PIX recebida", text);
        Assert.Contains("R$ 500,00", text);
        // Tab-separated columns within a row (design.md's "row/column text table" decision).
        Assert.Contains("Data\tDescricao\tValor", text);
    }
}
