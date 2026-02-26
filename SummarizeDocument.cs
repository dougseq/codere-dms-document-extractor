using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

public sealed class SummarizeDocument
{
    private static readonly Regex DateRegex = new(
        @"(?<!\d)(?<day>0?[1-9]|[12]\d|3[01])[/\-.](?<month>0?[1-9]|1[0-2])[/\-.](?<year>(19|20)?\d{2})(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] DateFormats =
    {
        "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "d.M.yyyy",
        "dd/MM/yy", "d/M/yy", "dd-MM-yy", "d-M-yy"
    };

    private static readonly string[] RevisionAnchors =
    {
        "revision", "renovacion", "actualizacion"
    };

    private static readonly string[] VigenciaAnchors =
    {
        "vigencia", "caducidad", "vencimiento", "validez", "vigente hasta", "hasta"
    };

    private static readonly Regex AutoridadLineRegex = new(
        @"(?i)\b(?:autoridad|organismo)\b\s*[:\-]?\s*(?<value>[^\r\n]+)",
        RegexOptions.Compiled);

    private static readonly Regex AutoridadEntidadRegex = new(
        @"(?i)\b(?<value>(?:Ayuntamiento\s+de|Ministerio\s+de|Agencia\s+(?:Nacional|Estatal|de)|Superintendencia\s+de|Direcci[oó]n\s+General\s+de|Comisi[oó]n\s+de|Consejer[ií]a\s+de|Gobernaci[oó]n\s+de|Municipalidad\s+de|Alcald[ií]a\s+de)\s+[^\r\n,;]{2,120})",
        RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly DocumentSummaryService _summaryService;
    private readonly DocumentTypePredictor _documentTypePredictor;
    private readonly ILogger _logger;

    public SummarizeDocument(
        DocumentSummaryService summaryService,
        DocumentTypePredictor documentTypePredictor,
        ILoggerFactory loggerFactory)
    {
        _summaryService = summaryService;
        _documentTypePredictor = documentTypePredictor;
        _logger = loggerFactory.CreateLogger<SummarizeDocument>();
    }

    [Function("SummarizeDocument")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "summarize-document")] HttpRequestData req)
    {
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<DocumentSummaryRequest>(body, JsonOptions);

            if (request is null || string.IsNullOrWhiteSpace(request.ContentBase64) || string.IsNullOrWhiteSpace(request.FileName))
                return await CreateBadRequest(req, "Se requiere FileName y ContentBase64.");

            var extension = Path.GetExtension(request.FileName)?.ToLowerInvariant();
            if (!_summaryService.IsSupportedExtension(extension))
                return await CreateBadRequest(req, "Formato no soportado. Usa .pdf, .docx, .pptx, .xlsx o .txt.");

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(request.ContentBase64);
            }
            catch (FormatException)
            {
                return await CreateBadRequest(req, "ContentBase64 no es valido.");
            }

            var result = await _summaryService.SummarizeAsync(request.FileName, bytes);
            var heading = string.Join(" ", result.Outline
                .Select(x => x.Heading)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeForSharePoint));
            var keyPoints = result.Outline
                .SelectMany(x => x.KeyPoints)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeForSharePoint)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
            var puntosClave = string.Join(" | ", keyPoints);
            var sumario = string.Join(" ", new[] { heading, NormalizeForSharePoint(result.StructuredSummary) }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            var tipoDocumento = _documentTypePredictor.PredictBestMatch(request.FileName, sumario, puntosClave);
            var sourceText = result.SourceText ?? $"{sumario} {puntosClave}";
            var autoridadOrganismo = PredictAutoridadOrganismo(sourceText);
            var fechaRevision = PredictDateOnly(sourceText, RevisionAnchors);
            var fechaVigencia = PredictDateOnly(sourceText, VigenciaAnchors);
            var pais = PredictPais(sourceText, autoridadOrganismo);

            var ok = req.CreateResponse(System.Net.HttpStatusCode.OK);
            ok.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await ok.WriteStringAsync(JsonSerializer.Serialize(
                new Dictionary<string, object?>
                {
                    ["Sumario"] = sumario,
                    ["PuntosClave"] = puntosClave,
                    ["Tipo de Documento"] = tipoDocumento,
                    ["Autoridad Organismo"] = string.IsNullOrWhiteSpace(autoridadOrganismo) ? null : NormalizeForSharePoint(autoridadOrganismo),
                    ["Fecha Revision"] = fechaRevision?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["Fecha Vigencia"] = fechaVigencia?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["Pais"] = pais
                },
                ResponseJsonOptions));
            return ok;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON de entrada invalido en SummarizeDocument");
            return await CreateBadRequest(req, "JSON invalido.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generando resumen esquematizado");
            var err = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
            await err.WriteStringAsync("Error interno procesando el documento.");
            return err;
        }
    }

    private static async Task<HttpResponseData> CreateBadRequest(HttpRequestData req, string message)
    {
        var bad = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
        await bad.WriteStringAsync(message);
        return bad;
    }

    private static string NormalizeForSharePoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value
            .Replace("\\r\\n", " ", StringComparison.Ordinal)
            .Replace("\\n", " ", StringComparison.Ordinal)
            .Replace("\\r", " ", StringComparison.Ordinal)
            .Replace("\\t", " ", StringComparison.Ordinal)
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal);

        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }

    private static string? PredictAutoridadOrganismo(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
            return null;

        var lines = SplitLines(sourceText);
        foreach (var line in lines)
        {
            var byLabel = AutoridadLineRegex.Match(line);
            if (byLabel.Success)
            {
                var value = CleanValue(TrimToFieldBoundary(byLabel.Groups["value"].Value));
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        var byEntity = AutoridadEntidadRegex.Match(sourceText);
        if (byEntity.Success)
        {
            var value = CleanValue(TrimToFieldBoundary(byEntity.Groups["value"].Value));
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static DateOnly? PredictDateOnly(string? sourceText, string[] anchors)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
            return null;

        var lines = SplitLines(sourceText).ToList();
        var date = FindDateNearAnchors(lines, anchors, window: 2);
        if (date.HasValue)
            return DateOnly.FromDateTime(date.Value);

        return null;
    }

    private static DateTime? FindDateNearAnchors(IReadOnlyList<string> lines, string[] anchors, int window)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (!ContainsAnyAnchor(lines[i], anchors))
                continue;

            var dateInLine = FindFirstDate(lines[i]);
            if (dateInLine.HasValue)
                return dateInLine;

            var from = Math.Max(0, i - window);
            var to = Math.Min(lines.Count - 1, i + window);
            for (var j = from; j <= to; j++)
            {
                if (j == i)
                    continue;

                var dateNearby = FindFirstDate(lines[j]);
                if (dateNearby.HasValue)
                    return dateNearby;
            }
        }

        return null;
    }

    private static DateTime? FindFirstDate(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var match = DateRegex.Match(line);
        if (match.Success && TryParseDate(match.Value, out var date))
            return date;

        return null;
    }

    private static bool TryParseDate(string dateText, out DateTime date)
    {
        var normalized = dateText.Replace('-', '/').Replace('.', '/');
        return DateTime.TryParseExact(
            normalized,
            DateFormats,
            CultureInfo.CreateSpecificCulture("es-ES"),
            DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces,
            out date);
    }

    private static bool ContainsAnyAnchor(string text, string[] anchors)
    {
        var foldedLine = FoldForMatch(text);
        foreach (var anchor in anchors)
        {
            var foldedAnchor = FoldForMatch(anchor);
            if (foldedLine.Contains(foldedAnchor, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string? PredictPais(string? sourceText, string? autoridadOrganismo)
    {
        var foldedText = FoldForMatch(sourceText);
        if (string.IsNullOrWhiteSpace(foldedText))
            return null;

        if (Regex.IsMatch(foldedText, @"\bargentina\b|\bargentin[oa]s?\b", RegexOptions.IgnoreCase)) return "Argentina";
        if (Regex.IsMatch(foldedText, @"\bcolombia\b|\bcolombian[oa]s?\b", RegexOptions.IgnoreCase)) return "Colombia";
        if (Regex.IsMatch(foldedText, @"\bespana\b|\bespanol(?:a|es)?\b", RegexOptions.IgnoreCase)) return "España";
        if (Regex.IsMatch(foldedText, @"\bitalia\b|\bitalian[oa]s?\b", RegexOptions.IgnoreCase)) return "Italia";
        if (Regex.IsMatch(foldedText, @"\bmexico\b|\bmexican[oa]s?\b", RegexOptions.IgnoreCase)) return "México";
        if (Regex.IsMatch(foldedText, @"\bpanama\b|\bpanamen[oa]s?\b", RegexOptions.IgnoreCase)) return "Panamá";
        if (Regex.IsMatch(foldedText, @"\buruguay\b|\buruguay[oa]s?\b", RegexOptions.IgnoreCase)) return "Uruguay";

        var foldedAutoridad = FoldForMatch(autoridadOrganismo);
        if (Regex.IsMatch(foldedAutoridad, @"\bayuntamiento\b|\bministerio\b|\bagencia\b|\bcomision\b", RegexOptions.IgnoreCase))
            return "España";

        if (Regex.IsMatch(foldedText, @"\bnif\b|\bcif\b|\bdni\b|\blopdgdd\b|\brgpd\b", RegexOptions.IgnoreCase))
            return "España";

        return null;
    }

    private static IEnumerable<string> SplitLines(string text) =>
        text.Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x));

    private static string TrimToFieldBoundary(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        var breakIndex = trimmed.IndexOfAny(new[] { '\r', '\n' });
        if (breakIndex >= 0)
            trimmed = trimmed[..breakIndex];

        foreach (var token in new[] { " revision", " vigencia", " fecha", " pais", " tipo", " documento" })
        {
            var index = trimmed.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                trimmed = trimmed[..index].TrimEnd();
                break;
            }
        }

        return trimmed;
    }

    private static string CleanValue(string value)
    {
        var clean = value.Trim();
        clean = Regex.Replace(clean, @"\s+", " ");
        return clean.Trim('.', ',', ';', ':', '-', '"', '\'', ' ');
    }

    private static string FoldForMatch(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var formD = input.Normalize(NormalizationForm.FormD);
        var buffer = new StringBuilder(formD.Length);

        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                buffer.Append(char.ToLowerInvariant(ch));
        }

        return buffer.ToString().Normalize(NormalizationForm.FormC);
    }
}
