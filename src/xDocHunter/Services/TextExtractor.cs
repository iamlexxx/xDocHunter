using System.IO;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace xDocHunter.Services;

public static class TextExtractor
{
    private const int MaxChars = 500_000;

    private static readonly HashSet<string> PlainTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".log", ".csv", ".tsv",
        ".xml", ".html", ".htm", ".xhtml",
        ".json", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf", ".config", ".properties", ".env",
        ".cs", ".vb", ".fs", ".fsx",
        ".js", ".ts", ".jsx", ".tsx", ".mjs", ".cjs",
        ".py", ".rb", ".php", ".go", ".rs", ".swift", ".kt", ".kts",
        ".java", ".scala", ".clj",
        ".c", ".cpp", ".h", ".hpp", ".cc",
        ".sql", ".sh", ".bash", ".zsh", ".fish", ".bat", ".cmd", ".ps1",
        ".r", ".rmd", ".tex", ".bib",
        ".css", ".scss", ".sass", ".less",
        ".gitignore", ".gitattributes", ".editorconfig", ".dockerignore"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".xlsx", ".pptx"
    };

    public static bool IsSupported(string extension) =>
        PlainTextExtensions.Contains(extension) || DocumentExtensions.Contains(extension);

    public static string Extract(string path, string extension)
    {
        try
        {
            if (PlainTextExtensions.Contains(extension))
                return ExtractPlainText(path);
            return extension.ToLowerInvariant() switch
            {
                ".pdf"  => ExtractPdf(path),
                ".docx" => ExtractDocx(path),
                ".xlsx" => ExtractXlsx(path),
                ".pptx" => ExtractPptx(path),
                _       => string.Empty
            };
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExtractPlainText(string path)
    {
        // Detect binary files by scanning for null bytes in the first 512 bytes.
        using (var fs = File.OpenRead(path))
        {
            var buf = new byte[Math.Min(512, (int)fs.Length)];
            int read = fs.Read(buf, 0, buf.Length);
            for (int i = 0; i < read; i++)
                if (buf[i] == 0) return string.Empty;
        }

        // UTF-8 with replacement (no throw on invalid bytes), fall back to Latin-1.
        try
        {
            var enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
            var text = File.ReadAllText(path, enc);
            return text.Length > MaxChars ? text[..MaxChars] : text;
        }
        catch
        {
            var text = File.ReadAllText(path, Encoding.Latin1);
            return text.Length > MaxChars ? text[..MaxChars] : text;
        }
    }

    private static string ExtractPdf(string path)
    {
        using var pdf = PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            sb.Append(page.Text);
            sb.Append(' ');
            if (sb.Length >= MaxChars) break;
        }
        return sb.Length > MaxChars ? sb.ToString(0, MaxChars) : sb.ToString();
    }

    private static string ExtractDocx(string path)
    {
        using var doc = WordprocessingDocument.Open(path, isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;
        var sb = new StringBuilder();
        foreach (var para in body.Descendants<Paragraph>())
        {
            sb.Append(para.InnerText);
            sb.Append(' ');
            if (sb.Length >= MaxChars) break;
        }
        return sb.Length > MaxChars ? sb.ToString(0, MaxChars) : sb.ToString();
    }

    private static string ExtractXlsx(string path)
    {
        using var doc = SpreadsheetDocument.Open(path, isEditable: false);
        var wb = doc.WorkbookPart;
        if (wb is null) return string.Empty;

        var sharedStrings = wb.SharedStringTablePart?.SharedStringTable
            ?.Elements<SharedStringItem>()
            .Select(i => i.InnerText)
            .ToArray() ?? [];

        var sb = new StringBuilder();
        foreach (var sheetPart in wb.WorksheetParts)
        {
            foreach (var cell in sheetPart.Worksheet.Descendants<Cell>())
            {
                string value;
                if (cell.DataType?.Value == CellValues.SharedString
                    && int.TryParse(cell.CellValue?.Text, out int idx)
                    && idx < sharedStrings.Length)
                {
                    value = sharedStrings[idx];
                }
                else
                {
                    value = cell.CellValue?.Text ?? string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(value))
                {
                    sb.Append(value);
                    sb.Append(' ');
                }
                if (sb.Length >= MaxChars) break;
            }
            if (sb.Length >= MaxChars) break;
        }
        return sb.Length > MaxChars ? sb.ToString(0, MaxChars) : sb.ToString();
    }

    private static string ExtractPptx(string path)
    {
        using var doc = PresentationDocument.Open(path, isEditable: false);
        var presentationPart = doc.PresentationPart;
        if (presentationPart is null) return string.Empty;
        var sb = new StringBuilder();
        foreach (var slidePart in presentationPart.SlideParts)
        {
            foreach (var text in slidePart.Slide.Descendants<DocumentFormat.OpenXml.Drawing.Text>())
            {
                if (!string.IsNullOrWhiteSpace(text.Text))
                {
                    sb.Append(text.Text);
                    sb.Append(' ');
                }
                if (sb.Length >= MaxChars) break;
            }
            if (sb.Length >= MaxChars) break;
        }
        return sb.Length > MaxChars ? sb.ToString(0, MaxChars) : sb.ToString();
    }
}
