using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

public sealed class RegexExtractors
{
    // Core Spanish date patterns: dd/mm/yyyy, d/m/yy, dd-mm-yyyy, dd.mm.yyyy
    private static readonly Regex DateRegex = new(
        @"(?<!\d)(?<day>0?[1-9]|[12]\d|3[01])[/\-.](?<month>0?[1-9]|1[0-2])[/\-.](?<year>(19|20)?\d{2})(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] DateFormats = new[]
    {
        "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy",
        "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yy", "d/M/yy", "dd-MM-yy", "d-M-yy"
    };

    // Anchors for contextual extraction
    private static readonly string[] CaducidadAnchors = new[] { "caduc", "vencim", "validez", "hasta" };
    private static readonly string[] ConcesionAnchors = new[] { "conces", "resoluci", "emisi", "otorga" };
    private static readonly string[] RenovacionAnchors = new[] { "renovac" };

    // Other fields
    private static readonly Regex ExpedienteRegex = new(@"(?i)\b(expediente|exp\.?)\s*[:\-]?\s*(?<exp>[\w\-\/\.]{6,40})", RegexOptions.Compiled);
    private static readonly Regex NifCifRegex = new(@"(?i)\b(NIF|CIF|NIE)\s*[:\-]?\s*(?<id>[A-Z0-9][0-9]{7,8}[A-Z0-9])", RegexOptions.Compiled);
    private static readonly Regex AyuntamientoRegex = new(@"(?i)\bAyuntamiento\s+de\s+(?<muni>[A-ZÁÉÍÓÚÑ][A-ZÁÉÍÓÚÑa-záéíóúñ\-\s]+)", RegexOptions.Compiled);
    private static readonly Regex DireccionRegex = new(@"(?i)\b(Direcci[oó]n|Domicilio|C/|Calle|Avda\.?|Avenida)\s*[:\-]?\s*(?<dir>.+)", RegexOptions.Compiled);
    private static readonly Regex ActividadRegex = new(@"(?i)\b(Actividad|Ep[ií]grafe|IAE)\s*[:\-]?\s*(?<act>.+)", RegexOptions.Compiled);

    public ExtractResult Extract(string fullText, string? ayuntamientoHint = null, string? muniHint = null)
    {
        var result = new ExtractResult();
        var lines = SplitLines(fullText);
        var lowered = fullText.ToLowerInvariant();

        // Expediente
        var mExp = ExpedienteRegex.Match(fullText);
        if (mExp.Success)
            result.Expediente = CleanValue(mExp.Groups["exp"].Value);

        // NIF/CIF
        var mId = NifCifRegex.Match(fullText);
        if (mId.Success)
            result.NIF_CIF = CleanValue(mId.Groups["id"].Value);

        // Ayuntamiento
        if (!string.IsNullOrWhiteSpace(ayuntamientoHint))
            result.Ayuntamiento = ayuntamientoHint;
        else
        {
            var mAyto = AyuntamientoRegex.Match(fullText);
            if (mAyto.Success)
                result.Ayuntamiento = mAyto.Groups["muni"].Value.Trim();
        }

        // Municipio
        if (!string.IsNullOrWhiteSpace(muniHint))
            result.Municipio = muniHint;

        // Dirección (first reasonable match, short-circuit excessive length)
        foreach (var line in lines)
        {
            var dm = DireccionRegex.Match(line);
            if (dm.Success)
            {
                var dir = dm.Groups["dir"].Value.Trim();
                if (dir.Length > 6 && dir.Length < 200)
                {
                    result.DireccionLocal = dir;
                    break;
                }
            }
        }

        // Actividad
        foreach (var line in lines)
        {
            var am = ActividadRegex.Match(line);
            if (am.Success)
            {
                var act = am.Groups["act"].Value.Trim();
                if (act.Length > 2 && act.Length < 200)
                {
                    result.Actividad = act;
                    break;
                }
            }
        }

        // Contextual dates
        result.FechaCaducidad = FindDateNearAnchors(lines, CaducidadAnchors, out var cadHints);
        result.FechaConcesion = FindDateNearAnchors(lines, ConcesionAnchors, out var concHints);
        result.FechaRenovacion = FindDateNearAnchors(lines, RenovacionAnchors, out var renHints);

        // Confidence heuristics
        double score = 0.0;
        int factors = 0;
        (double, string)? confCap(string? v, double w, string name) => 
            string.IsNullOrWhiteSpace(v) ? null : (w, name);

        (double, string)? confDate(DateTime? d, double w, string name) =>
            d.HasValue ? (w, name) : null;

        var parts = new List<(double,string)>();
        var e1 = confCap(result.Expediente, 0.25, "Expediente");
        var e2 = confDate(result.FechaConcesion, 0.25, "FechaConcesion");
        var e3 = confDate(result.FechaCaducidad, 0.35, "FechaCaducidad");
        var e4 = confCap(result.NIF_CIF, 0.15, "NIF/CIF");

        if (e1.HasValue) parts.Add(e1.Value);
        if (e2.HasValue) parts.Add(e2.Value);
        if (e3.HasValue) parts.Add(e3.Value);
        if (e4.HasValue) parts.Add(e4.Value);

        foreach (var p in parts) { score += p.Item1; factors++; }
        if (factors == 0) score = 0;
        result.ConfianzaExtraccion = Math.Round(score, 2);

        // Hints
        result.PalabrasClaveDetectadas.AddRange(cadHints);
        result.PalabrasClaveDetectadas.AddRange(concHints);
        result.PalabrasClaveDetectadas.AddRange(renHints);

        // Motivo revisión si hay inconsistencias
        var reasons = new List<string>();
        if (result.FechaConcesion.HasValue && result.FechaCaducidad.HasValue &&
            result.FechaCaducidad <= result.FechaConcesion)
            reasons.Add("La caducidad es anterior/igual a la concesión");

        if (result.ConfianzaExtraccion < 0.7)
            reasons.Add("Confianza baja (<0,7)");

        result.MotivoRevision = reasons.Count > 0 ? string.Join("; ", reasons) : null;

        // Resumen breve
        result.Resumen = BuildResumen(result);

        return result;
    }

    private static IEnumerable<string> SplitLines(string text) =>
        text.Replace("\r", "").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0);

    private static string CleanValue(string v) => v.Trim().Trim('.', ',', ';', ':', '-', '—', ' ');

    private static DateTime? FindDateNearAnchors(IEnumerable<string> lines, string[] anchors, out List<string> hints)
    {
        hints = new List<string>();
        var window = 3; // ±3 lines
        var list = lines.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var l = list[i];
            if (anchors.Any(a => l.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                // Search same line first
                var d = FindFirstDate(l);
                if (d.HasValue) { hints.Add(l); return d; }

                // Then search nearby lines
                for (int j = Math.Max(0, i - window); j <= Math.Min(list.Count - 1, i + window); j++)
                {
                    if (j == i) continue;
                    var d2 = FindFirstDate(list[j]);
                    if (d2.HasValue) { hints.Add(list[j]); return d2; }
                }
            }
        }
        // As fallback: first date in the whole document
        for (int i = 0; i < list.Count; i++)
        {
            var d = FindFirstDate(list[i]);
            if (d.HasValue) { hints.Add(list[i]); return d; }
        }
        return null;
    }

    private static DateTime? FindFirstDate(string line)
    {
        var m = DateRegex.Match(line);
        if (m.Success)
        {
            var raw = m.Value;
            if (TryParseSpanishDate(raw, out var dt))
                return dt;
        }
        return null;
    }

    private static bool TryParseSpanishDate(string s, out DateTime dt)
    {
        // Normalize separators to slash for parsing
        var norm = s.Replace('-', '/').Replace('.', '/');
        return DateTime.TryParseExact(
            norm, DateFormats, CultureInfo.CreateSpecificCulture("es-ES"),
            DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces, out dt);
    }

    private static string BuildResumen(ExtractResult r)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.Expediente)) parts.Add($"Expediente: {r.Expediente}");
        if (r.FechaConcesion.HasValue) parts.Add($"Concesión: {r.FechaConcesion:yyyy-MM-dd}");
        if (r.FechaCaducidad.HasValue) parts.Add($"Caducidad: {r.FechaCaducidad:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(r.Titular)) parts.Add($"Titular: {r.Titular}");
        if (!string.IsNullOrWhiteSpace(r.NIF_CIF)) parts.Add($"NIF/CIF: {r.NIF_CIF}");
        return string.Join(" · ", parts);
    }
}
