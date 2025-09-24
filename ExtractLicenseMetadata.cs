using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

public class ExtractLicenseMetadata
{
    private readonly DocIntelClient _doc;
    private readonly RegexExtractors _rx;
    private readonly ILogger _logger;

    public ExtractLicenseMetadata(DocIntelClient doc, RegexExtractors rx, ILoggerFactory loggerFactory)
    {
        _doc = doc;
        _rx = rx;
        _logger = loggerFactory.CreateLogger<ExtractLicenseMetadata>();
    }

    [Function("ExtractLicenseMetadata")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "extract")] HttpRequestData req)
    {
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<ExtractRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request is null || string.IsNullOrWhiteSpace(request.ContentBase64))
            {
                var bad = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await bad.WriteStringAsync("Missing ContentBase64");
                return bad;
            }

            var bytes = Convert.FromBase64String(request.ContentBase64);
            var fullText = await _doc.ExtractFullTextAsync(bytes);

            var result = _rx.Extract(fullText, request.AyuntamientoHint, request.MunicipalityHint);

            var ok = req.CreateResponse(System.Net.HttpStatusCode.OK);
            ok.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await ok.WriteStringAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting metadata");
            var err = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
            await err.WriteStringAsync($"Error: {ex.Message}");
            return err;
        }
    }
}
