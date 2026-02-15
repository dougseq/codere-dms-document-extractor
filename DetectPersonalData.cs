using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

public sealed class DetectPersonalData
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

    private readonly DocIntelClient _docIntel;
    private readonly PersonalDataDetector _detector;
    private readonly ILogger _logger;

    public DetectPersonalData(
        DocIntelClient docIntel,
        PersonalDataDetector detector,
        ILoggerFactory loggerFactory)
    {
        _docIntel = docIntel;
        _detector = detector;
        _logger = loggerFactory.CreateLogger<DetectPersonalData>();
    }

    [Function("DetectPersonalData")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "detect-personal-data")] HttpRequestData req)
    {
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<PersonalDataDetectionRequest>(body, JsonOptions);

            if (request is null || string.IsNullOrWhiteSpace(request.ContentBase64) || string.IsNullOrWhiteSpace(request.FileName))
                return await CreateBadRequest(req, "Se requiere FileName y ContentBase64.");

            var extension = Path.GetExtension(request.FileName)?.ToLowerInvariant();
            if (!IsSupportedExtension(extension))
                return await CreateBadRequest(req, "Formato no soportado. Usa .docx, .pdf, .xlsx o .txt.");

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(request.ContentBase64);
            }
            catch (FormatException)
            {
                return await CreateBadRequest(req, "ContentBase64 no es válido.");
            }

            var extractedText = await ExtractTextAsync(bytes, extension!);
            var result = _detector.Analyze(extractedText, extension!);

            var ok = req.CreateResponse(System.Net.HttpStatusCode.OK);
            ok.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await ok.WriteStringAsync(JsonSerializer.Serialize(result, ResponseJsonOptions));
            return ok;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON de entrada inválido en DetectPersonalData");
            return await CreateBadRequest(req, "JSON inválido.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detectando datos personales");
            var err = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
            await err.WriteStringAsync("Error interno procesando el documento.");
            return err;
        }
    }

    private async Task<string> ExtractTextAsync(byte[] content, string extension)
    {
        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            return DecodeTxt(content);

        return await _docIntel.ExtractFullTextAsync(content);
    }

    private static bool IsSupportedExtension(string? extension) =>
        extension is ".docx" or ".pdf" or ".xlsx" or ".txt";

    private static async Task<HttpResponseData> CreateBadRequest(HttpRequestData req, string message)
    {
        var bad = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
        await bad.WriteStringAsync(message);
        return bad;
    }

    private static string DecodeTxt(byte[] content)
    {
        var utf8 = Encoding.UTF8.GetString(content);
        if (!utf8.Contains('\uFFFD'))
            return utf8;

        return Encoding.Latin1.GetString(content);
    }
}
