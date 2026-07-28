using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DocumentIntelligence.Api.DocumentAnalysis;

/// <summary>
/// Claude's Messages API has no native binary support for DOCX/XLSX (only PDF and images), so
/// these are converted to plain text locally first via `DocumentFormat.OpenXml` — Microsoft's
/// own OOXML SDK — before being sent as a text content block (spec: document-analysis-service,
/// design.md's "native document/image blocks for PDF and images; local text extraction for
/// DOCX/XLSX" decision).
/// </summary>
public static class DocumentTextExtractor
{
    public static string ExtractDocxText(Stream stream)
    {
        using var wordDocument = WordprocessingDocument.Open(stream, isEditable: false);
        var body = wordDocument.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return "";
        }

        var paragraphs = body.Descendants<Paragraph>()
            .Select(paragraph => string.Concat(paragraph.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text)))
            .Where(text => !string.IsNullOrWhiteSpace(text));

        return string.Join('\n', paragraphs);
    }

    /// <summary>
    /// Reconstructs each worksheet as a simple row/column text table (tab-separated), giving
    /// the model tabular structure without needing spreadsheet-aware parsing on its side.
    /// </summary>
    public static string ExtractXlsxText(Stream stream)
    {
        using var spreadsheet = SpreadsheetDocument.Open(stream, isEditable: false);
        var workbookPart = spreadsheet.WorkbookPart;
        if (workbookPart is null)
        {
            return "";
        }

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable?
            .Elements<SharedStringItem>()
            .Select(item => item.InnerText)
            .ToArray() ?? [];

        var builder = new StringBuilder();
        foreach (var sheetPart in workbookPart.WorksheetParts)
        {
            var sheetData = sheetPart.Worksheet?.Elements<SheetData>().FirstOrDefault();
            if (sheetData is null)
            {
                continue;
            }

            foreach (var row in sheetData.Elements<Row>())
            {
                var cells = row.Elements<Cell>().Select(cell => ReadCellValue(cell, sharedStrings));
                builder.AppendLine(string.Join('\t', cells));
            }
        }

        return builder.ToString();
    }

    private static string ReadCellValue(Cell cell, IReadOnlyList<string> sharedStrings)
    {
        var raw = cell.CellValue?.Text ?? "";
        if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(raw, out var index) && index < sharedStrings.Count)
        {
            return sharedStrings[index];
        }

        return raw;
    }
}
