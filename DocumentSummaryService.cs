using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public sealed class DocumentSummaryService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "para", "como", "este", "esta", "estos", "estas", "desde", "hasta", "sobre", "entre", "donde",
        "cuando", "porque", "puede", "debe", "tiene", "tambien", "solo", "tras", "ante", "bajo",
        "mediante", "segun", "fue", "son", "ser", "han", "con", "sin", "por", "del", "las", "los",
        "una", "uno", "unos", "unas", "que", "sus", "se", "al", "el", "la", "en", "y", "o", "u",
        "the", "and", "for", "with", "this", "that", "from", "into", "have", "has", "were", "been",
        "shall", "will", "can", "could", "would", "should", "your", "their", "about", "within",
        "documento", "doc", "archivo", "anexo", "capitulo", "seccion", "apartado", "pagina"
    };

    private readonly DocIntelClient _docIntel;

    public DocumentSummaryService(DocIntelClient docIntel)
    {
        _docIntel = docIntel;
    }

    public bool IsSupportedExtension(string? extension) =>
        extension is ".pdf" or ".docx" or ".pptx" or ".xlsx" or ".txt";

    public async Task<DocumentSummaryResult> SummarizeAsync(string fileName, byte[] content)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;
        var rawText = await ExtractTextAsync(content, extension);
        var normalizedText = NormalizeText(rawText);

        var result = new DocumentSummaryResult
        {
            FileType = extension,
            TextLength = normalizedText.Length,
            DocumentTitle = ExtractTitle(normalizedText, fileName),
            SourceText = normalizedText
        };

        if (normalizedText.Length < 40)
        {
            result.ReviewReason = "No se pudo extraer texto suficiente para resumir el documento.";
            result.StructuredSummary = BuildStructuredSummary(result, fileName);
            return result;
        }

        result.KeywordsDetected = ExtractKeywords(normalizedText, 8);
        result.Outline = BuildOutline(normalizedText);

        if (result.Outline.Count == 0)
        {
            result.ReviewReason = "No se detectaron secciones claras; se genero un resumen general.";
            result.Outline = BuildFallbackOutline(normalizedText);
        }

        result.StructuredSummary = BuildStructuredSummary(result, fileName);
        return result;
    }

    private async Task<string> ExtractTextAsync(byte[] content, string extension)
    {
        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            return DecodeTxt(content);

        return await _docIntel.ExtractFullTextAsync(content);
    }

    private static List<DocumentSummarySection> BuildOutline(string text)
    {
        var lines = text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Regex.Replace(x, @"\s+", " ").Trim())
            .Where(x => x.Length > 0)
            .Take(1200)
            .ToList();

        var sections = new List<(string Heading, List<string> Lines)>();
        var currentHeading = "Resumen general";
        var currentLines = new List<string>();

        foreach (var line in lines)
        {
            if (IsHeading(line))
            {
                FlushSectionIfNeeded(sections, currentHeading, currentLines);
                currentHeading = CleanHeading(line);
                currentLines = new List<string>();
                continue;
            }

            currentLines.Add(line);
        }

        FlushSectionIfNeeded(sections, currentHeading, currentLines);

        return sections
            .Select(section => new DocumentSummarySection
            {
                Heading = section.Heading,
                KeyPoints = ExtractKeyPoints(string.Join(" ", section.Lines), 3)
            })
            .Where(x => x.KeyPoints.Count > 0)
            .Take(6)
            .ToList();
    }

    private static List<DocumentSummarySection> BuildFallbackOutline(string text)
    {
        var keyPoints = ExtractKeyPoints(text, 5);
        if (keyPoints.Count == 0)
            return new List<DocumentSummarySection>();

        return new List<DocumentSummarySection>
        {
            new()
            {
                Heading = "Resumen general",
                KeyPoints = keyPoints
            }
        };
    }

    private static void FlushSectionIfNeeded(List<(string Heading, List<string> Lines)> sections, string heading, List<string> lines)
    {
        if (lines.Count == 0)
            return;

        sections.Add((heading, lines));
    }

    private static bool IsHeading(string line)
    {
        if (line.Length is < 4 or > 100)
            return false;

        if (Regex.IsMatch(line, @"^\d+(\.\d+){0,3}\s+.+"))
            return true;

        if (Regex.IsMatch(line, @"^[IVXLCDM]+\.\s+.+", RegexOptions.IgnoreCase))
            return true;

        if (line.EndsWith(":", StringComparison.Ordinal))
            return true;

        var letters = line.Count(char.IsLetter);
        var upperLetters = line.Count(char.IsUpper);
        if (letters >= 6 && upperLetters >= (int)Math.Round(letters * 0.75, MidpointRounding.AwayFromZero) && line.Count(char.IsWhiteSpace) <= 10)
            return true;

        return false;
    }

    private static string CleanHeading(string line)
    {
        var cleaned = line.Trim().TrimEnd(':').Trim();
        if (cleaned.Length <= 80)
            return cleaned;
        return cleaned[..80];
    }

    private static List<string> ExtractKeyPoints(string text, int maxPoints)
    {
        var candidates = Regex
            .Split(text, @"(?<=[\.\!\?;])\s+|\r?\n+")
            .Select((value, index) => new
            {
                Index = index,
                Value = Regex.Replace(value, @"\s+", " ").Trim()
            })
            .Where(x => x.Value.Length is >= 30 and <= 260)
            .ToList();

        if (candidates.Count == 0)
            return new List<string>();

        var topTerms = ExtractKeywords(text, 12);
        var ranked = candidates
            .Select(x => new
            {
                x.Index,
                x.Value,
                Score = ScoreSentence(x.Value, topTerms)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .Take(maxPoints * 2)
            .OrderBy(x => x.Index)
            .ToList();

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var item in ranked)
        {
            if (!unique.Add(item.Value))
                continue;

            result.Add(item.Value);
            if (result.Count >= maxPoints)
                break;
        }

        return result;
    }

    private static int ScoreSentence(string sentence, IReadOnlyCollection<string> topTerms)
    {
        var score = 0;

        foreach (var term in topTerms)
        {
            if (sentence.Contains(term, StringComparison.OrdinalIgnoreCase))
                score++;
        }

        if (Regex.IsMatch(sentence, @"\d"))
            score++;

        if (sentence.Length is >= 60 and <= 180)
            score++;

        return score;
    }

    private static List<string> ExtractKeywords(string text, int max)
    {
        var matches = Regex.Matches(text.ToLowerInvariant(), @"\b[\p{L}\p{N}]{4,}\b");
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in matches)
        {
            var token = match.Value.Trim();
            if (StopWords.Contains(token))
                continue;

            if (counts.TryGetValue(token, out var count))
                counts[token] = count + 1;
            else
                counts[token] = 1;
        }

        return counts
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Value >= 2)
            .Select(x => x.Key)
            .Take(max)
            .ToList();
    }

    private static string ExtractTitle(string text, string fileName)
    {
        var firstLine = text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .FirstOrDefault(x => x.Length > 5);

        if (!string.IsNullOrWhiteSpace(firstLine) && firstLine.Length <= 120)
            return firstLine;

        return Path.GetFileName(fileName);
    }

    private static string BuildStructuredSummary(DocumentSummaryResult result, string fileName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Documento: {result.DocumentTitle ?? Path.GetFileName(fileName)}");
        sb.AppendLine($"Tipo: {result.FileType}");
        sb.AppendLine($"Longitud de texto analizado: {result.TextLength} caracteres");

        if (result.KeywordsDetected.Count > 0)
            sb.AppendLine($"Temas clave: {string.Join(", ", result.KeywordsDetected)}");

        sb.AppendLine("Esquema:");
        if (result.Outline.Count == 0)
        {
            sb.AppendLine("1. Sin contenido estructurable.");
        }
        else
        {
            var index = 1;
            foreach (var section in result.Outline)
            {
                sb.AppendLine($"{index}. {section.Heading}");
                foreach (var point in section.KeyPoints)
                    sb.AppendLine($"- {point}");

                index++;
            }
        }

        if (!string.IsNullOrWhiteSpace(result.ReviewReason))
            sb.AppendLine($"Revision: {result.ReviewReason}");

        return sb.ToString().Trim();
    }

    private static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return Regex.Replace(text, @"[ \t]+", " ").Trim();
    }

    private static string DecodeTxt(byte[] content)
    {
        var utf8 = Encoding.UTF8.GetString(content);
        if (!utf8.Contains('\uFFFD'))
            return utf8;

        return Encoding.Latin1.GetString(content);
    }
}
