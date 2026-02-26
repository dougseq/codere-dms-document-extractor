using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

public sealed class ExtractRequest
{
    public string? FileName { get; set; }
    public string? ContentBase64 { get; set; }
    public string? AyuntamientoHint { get; set; } // opcional
    public string? MunicipalityHint { get; set; } // opcional
}

public sealed class ExtractResult
{
    public string? Expediente { get; set; }
    public string? Ayuntamiento { get; set; }
    public string? Municipio { get; set; }
    public string? Titular { get; set; }
    public string? NIF_CIF { get; set; }
    public string? DireccionLocal { get; set; }
    public string? Actividad { get; set; }
    public DateTime? FechaConcesion { get; set; }
    public DateTime? FechaCaducidad { get; set; }
    public DateTime? FechaRenovacion { get; set; }
    public double ConfianzaExtraccion { get; set; }
    public string? MotivoRevision { get; set; }
    public List<string> PalabrasClaveDetectadas { get; set; } = new();
    public string? Resumen { get; set; }

    [JsonPropertyName("GD_AutoridadOrganismo")]
    public string? GD_AutoridadOrganismo { get; set; }

    [JsonPropertyName("GD_FechaRevision")]
    public DateOnly? GD_FechaRevision { get; set; }

    [JsonPropertyName("GD_FechaVigencia")]
    public DateOnly? GD_FechaVigencia { get; set; }

    [JsonPropertyName("GD_Pais")]
    public string? GD_Pais { get; set; }
}

public sealed class PersonalDataDetectionRequest
{
    public string? FileName { get; set; }
    public string? ContentBase64 { get; set; }
}

public sealed class PersonalDataDetectionResult
{
    public string? FileType { get; set; }
    public bool ContainsPersonalData { get; set; }
    public bool ContainsSpecialCategoryData { get; set; }
    public double Score { get; set; }
    public int TextLength { get; set; }
    public List<string> CategoriesDetected { get; set; } = new();
    public List<string> Indicators { get; set; } = new();
    public string? ReviewReason { get; set; }
    public string? Summary { get; set; }
}

public sealed class DocumentSummaryRequest
{
    public string? FileName { get; set; }
    public string? ContentBase64 { get; set; }
}

public sealed class DocumentSummarySection
{
    public string? Heading { get; set; }
    public List<string> KeyPoints { get; set; } = new();
}

public sealed class DocumentSummaryResult
{
    public string? FileType { get; set; }
    public int TextLength { get; set; }
    public string? DocumentTitle { get; set; }
    public string? SourceText { get; set; }
    public List<string> KeywordsDetected { get; set; } = new();
    public List<DocumentSummarySection> Outline { get; set; } = new();
    public string? StructuredSummary { get; set; }
    public string? ReviewReason { get; set; }
}
