using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace PKeetDashboard.API.Services;

public class ResumeTextExtractor
{
    private const int MaxCharsForAi = 28_000;

    public string ExtractPlainText(byte[] content, string fileName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        string raw = ext switch
        {
            ".pdf" => ExtractFromPdf(content),
            ".docx" => ExtractFromDocx(content),
            ".doc" => throw new InvalidOperationException(
                "Legacy .doc format is not supported. Please save as .docx or PDF and upload again."),
            _ => throw new InvalidOperationException($"Unsupported file type '{ext}'. Use PDF or DOCX."),
        };

        raw = NormalizeWhitespace(raw);
        if (raw.Length > MaxCharsForAi)
        {
            var head = raw[..(MaxCharsForAi / 2)];
            var tail = raw[^((MaxCharsForAi / 2) - 200)..];
            raw = head + "\n\n[... middle truncated for processing ...]\n\n" + tail;
        }

        return raw;
    }

    public static string GuessMimeType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            _ => "application/octet-stream",
        };
    }

    private static string ExtractFromPdf(byte[] content)
    {
        using var ms = new MemoryStream(content, writable: false);
        using var pdf = PdfDocument.Open(ms);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            var text = page.Text;
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (sb.Length > 0) sb.AppendLine().AppendLine();
            sb.Append(text);
        }

        var result = sb.ToString();
        return string.IsNullOrWhiteSpace(result)
            ? ""
            : result;
    }

    private static string ExtractFromDocx(byte[] content)
    {
        using var ms = new MemoryStream(content, writable: false);
        using var doc = WordprocessingDocument.Open(ms, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return "";

        var sb = new StringBuilder();
        foreach (var block in body.Elements())
        {
            if (block is Paragraph para)
            {
                foreach (var run in para.Elements<Run>())
                {
                    foreach (var text in run.Elements<Text>())
                    {
                        if (!string.IsNullOrEmpty(text.Text))
                            sb.Append(text.Text);
                    }
                }

                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string NormalizeWhitespace(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var lines = s.Split('\n', StringSplitOptions.None);
        var trimmed = lines.Select(l => l.TrimEnd());
        return string.Join("\n", trimmed).Trim();
    }
}
