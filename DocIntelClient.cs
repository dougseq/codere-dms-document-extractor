using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public sealed class DocIntelClient
{
    private readonly string _endpoint;
    private readonly string _key;
    private readonly string _defaultLang;

    public DocIntelClient(IConfiguration config)
    {
        _endpoint = config["DOCINTEL_ENDPOINT"] ?? throw new InvalidOperationException("DOCINTEL_ENDPOINT not set");
        _key = config["DOCINTEL_KEY"] ?? throw new InvalidOperationException("DOCINTEL_KEY not set");
        _defaultLang = config["DEFAULT_LANGUAGE"] ?? "es";
    }

    public async Task<string> ExtractFullTextAsync(byte[] content)
    {
        var credential = new AzureKeyCredential(_key);
        var client = new DocumentIntelligenceClient(new Uri(_endpoint), credential);

        // GA 1.0.0: Use AnalyzeDocumentOptions(modelId, BinaryData) + Locale
        var options = new AnalyzeDocumentOptions("prebuilt-read", BinaryData.FromBytes(content))
        {
            Locale = _defaultLang
        };

        var operation = await client.AnalyzeDocumentAsync(Azure.WaitUntil.Completed, options);
        var result = operation.Value;

        // Prefer building from lines if available
        var sb = new StringBuilder();
        if (result.Pages is not null)
        {
            foreach (var page in result.Pages)
            {
                if (page.Lines is not null)
                {
                    foreach (var line in page.Lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line.Content))
                            sb.AppendLine(line.Content);
                    }
                }
            }
        }

        if (sb.Length == 0 && !string.IsNullOrWhiteSpace(result.Content))
            return result.Content;

        return sb.ToString();
    }
}
