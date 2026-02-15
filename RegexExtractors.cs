using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

public sealed class RegexExtractors
{
    private static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var t = s.Replace('\u2013','-').Replace('\u2014','-');
        t = Regex.Replace(t, @"\s+", " ").Trim();
        return t;
    }

    private static readonly Regex DateRegex = new(
        @"(?<!\d)(?<day>0?[1-9]|[12]\d|3[01])[/\-.](?<month>0?[1-9]|1[0-2])[/\-.](?<year>(19|20)?\d{2})(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] DateFormats = new[]
    {
        "dd/MM/yyyy","d/M/yyyy","dd-MM-yyyy","d-M-yyyy","dd.MM.yyyy","d.M.yyyy",
        "dd/MM/yy","d/M/yy","dd-MM-yy","d-M-yy"
    };

    private static readonly string[] CaducidadAnchors = { "caduc", "vencim", "validez", "hasta" };
    private static readonly string[] ConcesionAnchors = { "conces", "resoluci", "emisi", "otorga" };
    private static readonly string[] RenovacionAnchors = { "renovac" };

    private static readonly Regex ExpedienteLineRegex = new(
    @"(?ix)                                   # ignorecase + verbose
      \b
      (?:                                     # variantes de EXPEDIENTE
         expediente | expdte | expedte | expte | exp\.?
      )
      \b
      [\s:;""'\u00AB\u00BB\-–—\.]*            # separadores comunes
      (?:                                     # sufijo opcional tipo número
         (?:n[\u00BA\u00B0])
         | n(?:\u00FAm\.?|um\.?)
         | \u00FAn\.?
         | n\u00FAmero
         | numero
         | no\.?
      )?
      [\s\-]*                                 # separador extra antes del código
      (?<exp>                                 # el código real
         [A-Z0-9] [A-Z0-9\./\-]{3,80}
      )
    ",
    RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);
    private static readonly Regex NifCifLabeledRegex = new(
        @"(?i)\b(C\.?\s?I\.?\s?F\.?|CIF|N\.?\s?I\.?\s?F\.?|NIF|N\.?\s?I\.?\s?E\.?|NIE)\b\s*[:\-]?\s*(?<id>[A-Z0-9][\s\-]?\d{7,8}[\s\-]?[A-Z0-9])",
        RegexOptions.Compiled);

    private static readonly Regex NifCifGenericRegex = new(
        @"(?i)\b([A-Z]\s?\d{7}\s?[A-Z]|[0-9]\s?\d{7}\s?[A-Z]|[XYZ]\s?\d{7}\s?[A-Z])\b",
        RegexOptions.Compiled);

    private static readonly Regex AyuntamientoRegex = new(
        @"(?i)\bAyuntamiento\s+de\s+(?<muni>[^\r\n:,;]{2,80})",
        RegexOptions.Compiled);
    private static readonly Regex MunicipioRegex = new(
        @"(?i)\b(Municipio|Localidad|Poblaci\u00F3n)\b.*?[:\-]?\s*(?<muni>[\p{L}\s\.\-]{2,50})",
        RegexOptions.Compiled);

    private static readonly Regex DireccionRegex = new(
        @"(?i)\b(Direcci\u00F3n|Domicilio|C/|Calle|Avda\.?|Avenida)\s*[:\-]?\s*(?<dir>.+)",
        RegexOptions.Compiled);
    private static readonly Regex ActividadRegex = new(
        @"(?i)\b(Actividad(?:es)?(?:\s+Econ\u00F3mica(?:s)?)?|Ep\u00EDgrafe\s*IAE|IAE|CNAE|Objeto)\b[:\-]?\s*(?<act>.+)",
        RegexOptions.Compiled);
    private static readonly Regex TitularRegex = new(
        @"(?i)\b(Titular(?:\s+de\s+la\s+actividad)?|Representante|Solicitante|Interesado|Empresa|Raz\u00F3n\s+Social|Denominaci\u00F3n\s+Social)\b\s*[:\-]?\s*(?<name>.+)",
        RegexOptions.Compiled);

    public ExtractResult Extract(string fullText, string? ayuntamientoHint = null, string? muniHint = null)
    {
        var result = new ExtractResult();
        var normalized = Normalize(fullText);
        var lines = SplitLines(fullText).ToList();

        var hint = SanitizeHintPath(ayuntamientoHint);
        if (!string.IsNullOrWhiteSpace(hint))
            result.Ayuntamiento = hint;

        var mAyto = AyuntamientoRegex.Match(fullText);
        if (mAyto.Success)
            result.Ayuntamiento = TrimToFieldBoundary(mAyto.Groups["muni"].Value);

        if (!string.IsNullOrWhiteSpace(muniHint))
            result.Municipio = muniHint;
        else
        {
            var mMun = MunicipioRegex.Match(fullText);
            if (mMun.Success)
                result.Municipio = TrimToFieldBoundary(mMun.Groups["muni"].Value);
        }

        var mExp = ExpedienteLineRegex.Match(normalized);
        if (mExp.Success)
            result.Expediente = CleanExp(mExp.Groups["exp"].Value);
        else
        {
            var exp = FindCodeAfterAnchor(lines, new[] { "expediente", "expte", "exped" });
            if (!string.IsNullOrWhiteSpace(exp))
                result.Expediente = CleanExp(exp);
        }

        var mId = NifCifLabeledRegex.Match(normalized);
        if (mId.Success)
            result.NIF_CIF = CleanValue(mId.Groups["id"].Value);
        else
        {
            var mg = NifCifGenericRegex.Match(normalized);
            if (mg.Success)
                result.NIF_CIF = CleanValue(mg.Value);
        }

        foreach (var line in lines)
        {
            var tm = TitularRegex.Match(line);
            if (tm.Success)
            {
                var name = TrimUntilTokens(tm.Groups["name"].Value, new[] { "NIF", "CIF", "NIE" });
                if (name.Length > 2 && name.Length < 120)
                {
                    result.Titular = name;
                    break;
                }
            }
        }

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

        foreach (var line in lines)
        {
            var am = ActividadRegex.Match(line);
            if (am.Success)
            {
                var act = TrimUntilTokens(am.Groups["act"].Value, new[] { "IAE", "CNAE", "NIF", "CIF" });
                act = CleanValue(act);
                if (act.Length > 3 && act.Length < 200)
                {
                    result.Actividad = act;
                    break;
                }
            }
        }

        result.FechaCaducidad = FindDateNearAnchors(lines, CaducidadAnchors, out var cadHints, allowFallback:false);
        result.FechaConcesion = FindDateNearAnchors(lines, ConcesionAnchors, out var concHints, allowFallback:false);
        result.FechaRenovacion = FindDateNearAnchors(lines, RenovacionAnchors, out var renHints, allowFallback:false);
        if (!result.FechaCaducidad.HasValue)
        {
            var allDates = FindAllDates(lines);
            if (allDates.Count == 1) result.FechaCaducidad = allDates[0];
        }

        result.ConfianzaExtraccion = ComputeConfidence(result, mAyto.Success);

        var reasons = new List<string>();
        if (result.FechaConcesion.HasValue && result.FechaCaducidad.HasValue &&
            result.FechaCaducidad <= result.FechaConcesion)
            reasons.Add("La caducidad es anterior/igual a la concesión");
        if (result.Expediente is null || result.Expediente.Length < 6)
            reasons.Add("Expediente no fiable");
        if (string.IsNullOrWhiteSpace(result.NIF_CIF))
            reasons.Add("NIF/CIF no detectado");
        result.MotivoRevision = reasons.Count > 0 ? string.Join("; ", reasons) : null;

        result.PalabrasClaveDetectadas.AddRange(cadHints);
        result.PalabrasClaveDetectadas.AddRange(concHints);
        result.PalabrasClaveDetectadas.AddRange(renHints);
        result.Resumen = BuildResumen(result);

        return result;
    }

    private static IEnumerable<string> SplitLines(string text) =>
        text.Replace("\r", "").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0);

    private static string CleanValue(string v)
    {
        var t = v.Trim();
        t = Regex.Replace(t, @"\s+", " ");
        t = t.Trim('.', ',', ';', ':', '-', '\u2013', '\u2014', '"', '\'', ' ');
        return t;
    }

    private static string TrimToFieldBoundary(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        var breakIndex = trimmed.IndexOfAny(new[] { '\r', '\n' });
        if (breakIndex >= 0)
            trimmed = trimmed[..breakIndex];

        foreach (var token in new[] { " expediente", " municipio", " titular", " dirección", " actividad", " nif", " cif" })
        {
            var index = trimmed.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                trimmed = trimmed[..index].TrimEnd();
                break;
            }
        }

        return CleanValue(trimmed);
    }

    private static string CleanExp(string v)
    {
        var t = CleanValue(v).ToUpperInvariant();
        // recorta justo antes de cualquier texto no válido al final
        var m = Regex.Match(t, @"^[A-Z0-9][A-Z0-9\./\-]{3,80}");
        return m.Success ? m.Value : t;
    }


    private static string TrimUntilTokens(string input, string[] tokens)
    {
        var t = input;
        foreach (var tok in tokens)
        {
            var idx = t.IndexOf(tok, StringComparison.OrdinalIgnoreCase);
            if (idx > 0) t = t.Substring(0, idx);
        }
        return t.Trim();
    }

    private static string? SanitizeHintPath(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint)) return null;
        var s = hint.Replace("\\","/");
        var parts = s.Split(new[]{'/'}, StringSplitOptions.RemoveEmptyEntries).ToList();
        var noise = new HashSet<string>(StringComparer.OrdinalIgnoreCase){
            "documentos compartidos","shared documents","forms","licencias","documents","sites","siteassets"
        };
        for (int i = parts.Count-1; i>=0; i--)
        {
            var p = parts[i].Trim();
            if (p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
            if (!noise.Contains(p.ToLowerInvariant())) return p;
        }
        return parts.LastOrDefault()?.Trim();
    }

    private static string? FindCodeAfterAnchor(List<string> lines, string[] anchors)
    {
        // Un código “bueno” debe tener dígitos y, preferiblemente, separadores tipo / . -
        bool LooksLikeCode(string s) =>
            s.Length >= 5 &&
            Regex.IsMatch(s, @"\d") &&
            Regex.IsMatch(s, @"[./\-]");

        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (anchors.Any(a => l.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                // 1) Mismo renglón tras el ancla
                var after = Regex.Replace(l, @"(?i).*\b(expediente|expdte|expedte|expte|exp\.?)\b[:;""'«»–—\-\.\s]*", "");
                after = StripLeadingCodeToken(after);
                var m1 = Regex.Match(after, @"([A-Z0-9][A-Z0-9\./\-]{3,80})", RegexOptions.IgnoreCase);
                if (m1.Success && LooksLikeCode(m1.Value)) return m1.Value;

                // 2) Línea siguiente
                if (i + 1 < lines.Count)
                {
                    var next = StripLeadingCodeToken(lines[i + 1]);
                    var m2 = Regex.Match(next, @"([A-Z0-9][A-Z0-9\./\-]{3,80})", RegexOptions.IgnoreCase);
                    if (m2.Success && LooksLikeCode(m2.Value)) return m2.Value;
                }
            }
        }
        return null;
    }

    private static string StripLeadingCodeToken(string s) =>
        Regex.Replace(s, @"^(?:n[\u00BA\u00B0]|n(?:\u00FAm|um)?\.?)\s+", "", RegexOptions.IgnoreCase);


    private static DateTime? FindDateNearAnchors(IEnumerable<string> lines, string[] anchors, out List<string> hints, bool allowFallback)
    {
        hints = new List<string>();
        var window = 3;
        var list = lines.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var l = list[i];
            if (anchors.Any(a => l.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var d = FindFirstDate(l);
                if (d.HasValue) { hints.Add(l); return d; }
                for (int j = Math.Max(0, i - window); j <= Math.Min(list.Count - 1, i + window); j++)
                {
                    if (j == i) continue;
                    var d2 = FindFirstDate(list[j]);
                    if (d2.HasValue) { hints.Add(list[j]); return d2; }
                }
            }
        }
        if (allowFallback)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var d = FindFirstDate(list[i]);
                if (d.HasValue) { hints.Add(list[i]); return d; }
            }
        }
        return null;
    }

    private static List<DateTime> FindAllDates(IEnumerable<string> lines)
    {
        var res = new List<DateTime>();
        foreach (var l in lines)
        {
            var m = DateRegex.Match(l);
            if (m.Success && TryParseSpanishDate(m.Value, out var dt)) res.Add(dt);
        }
        return res;
    }

    private static DateTime? FindFirstDate(string line)
    {
        var m = DateRegex.Match(line);
        if (m.Success && TryParseSpanishDate(m.Value, out var dt)) return dt;
        return null;
    }

    private static bool TryParseSpanishDate(string s, out DateTime dt)
    {
        var norm = s.Replace('-', '/').Replace('.', '/');
        return DateTime.TryParseExact(norm, DateFormats, CultureInfo.CreateSpecificCulture("es-ES"),
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
        return string.Join(" | ", parts);
    }

    private static double ComputeConfidence(ExtractResult r, bool aytoFromDoc)
    {
        double score = 0;
        if (!string.IsNullOrWhiteSpace(r.Expediente) && r.Expediente.Length >= 7) score += 0.30;
        if (r.FechaConcesion.HasValue) score += 0.25;
        if (r.FechaCaducidad.HasValue) score += 0.30;
        if (!string.IsNullOrWhiteSpace(r.NIF_CIF)) score += 0.10;
        if (aytoFromDoc) score += 0.05;

        if (r.FechaConcesion.HasValue && r.FechaCaducidad.HasValue && r.FechaCaducidad <= r.FechaConcesion) score -= 0.20;
        if (string.IsNullOrWhiteSpace(r.Expediente) || r.Expediente.Length < 7) score -= 0.10;

        if (score < 0) score = 0;
        if (score > 1) score = 1;
        return Math.Round(score, 2);
    }
}

