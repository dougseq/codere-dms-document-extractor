using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

public sealed class SummarizeDocument
{
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

            var ok = req.CreateResponse(System.Net.HttpStatusCode.OK);
            ok.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await ok.WriteStringAsync(JsonSerializer.Serialize(
                new Dictionary<string, object>
                {
                    ["Sumario"] = sumario,
                    ["PuntosClave"] = puntosClave,
                    ["Tipo de Documento"] = tipoDocumento
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
}
