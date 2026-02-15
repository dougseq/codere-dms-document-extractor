using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

public sealed class PersonalDataDetector
{
    private sealed record DetectionRule(
        string Category,
        Regex Pattern,
        double Weight,
        bool IsSpecialCategory);

    private static readonly DetectionRule[] Rules =
    {
        new(
            "Identificativo",
            new Regex(@"\b(?:\d{8}[A-HJ-NP-TV-Z]|[XYZ]\d{7}[A-Z]|[A-HJNP-SUVW]\d{7}[0-9A-J])\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            0.35,
            false),
        new(
            "Contacto",
            new Regex(@"\b[a-z0-9._%+\-]+@[a-z0-9.\-]+\.[a-z]{2,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            0.20,
            false),
        new(
            "Contacto",
            new Regex(@"\b(?:\+34[\s\-]?)?(?:6\d{2}|7[1-9]\d|8\d{2}|9\d{2})[\s\-]?\d{3}[\s\-]?\d{3}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            0.20,
            false),
        new(
            "Direcciones",
            new Regex(@"\b(?:domicilio|direcci[oó]n|calle|avenida|avda\.?|plaza|c\/)\b.{0,90}", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            0.15,
            false),
        new(
            "Financiero",
            new Regex(@"\bES\d{2}[A-Z0-9]{20}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            0.30,
            false),
        new(
            "Especial",
            new Regex(@"\b(?:salud|historia cl[ií]nica|diagn[oó]stico|tratamiento m[eé]dico|baja m[eé]dica|discapacidad|minusval[ií]a)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            0.40,
            true),
        new(
            "Especial",
            new Regex(@"\b(?:biom[eé]trico|huella dactilar|reconocimiento facial|adn)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            0.45,
            true),
        new(
            "Especial",
            new Regex(@"\b(?:ideolog[ií]a|opini[oó]n pol[ií]tica|afiliaci[oó]n sindical|religi[oó]n|creencias|orientaci[oó]n sexual|vida sexual|origen racial|etnia)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            0.45,
            true),
        new(
            "Especial",
            new Regex(@"\b(?:condena penal|antecedentes penales|infracci[oó]n penal)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            0.45,
            true),
    };

    public PersonalDataDetectionResult Analyze(string? text, string fileType)
    {
        var result = new PersonalDataDetectionResult
        {
            FileType = fileType,
            TextLength = string.IsNullOrWhiteSpace(text) ? 0 : text.Length
        };

        if (string.IsNullOrWhiteSpace(text))
        {
            result.ContainsPersonalData = false;
            result.Score = 0;
            result.ReviewReason = "No se pudo extraer texto para analizar.";
            result.Summary = "Sin texto analizable.";
            return result;
        }

        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var indicators = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var score = 0d;
        var special = false;

        foreach (var rule in Rules)
        {
            var matches = rule.Pattern.Matches(text);
            if (matches.Count == 0)
                continue;

            categories.Add(rule.Category);
            score += rule.Weight;
            if (rule.IsSpecialCategory)
                special = true;

            foreach (Match match in matches.Cast<Match>().Take(3))
            {
                var indicator = CleanIndicator(match.Value);
                if (!string.IsNullOrWhiteSpace(indicator))
                    indicators.Add(indicator);
            }
        }

        // Potential card numbers are validated with Luhn to reduce false positives.
        foreach (Match candidate in Regex.Matches(text, @"\b(?:\d[ -]?){13,19}\b", RegexOptions.Compiled))
        {
            var digits = new string(candidate.Value.Where(char.IsDigit).ToArray());
            if (digits.Length is < 13 or > 19)
                continue;
            if (!PassesLuhn(digits))
                continue;

            categories.Add("Financiero");
            indicators.Add(CleanIndicator(candidate.Value));
            score += 0.30;
            break;
        }

        if (categories.Count >= 2)
            score += 0.10;

        score = Math.Clamp(score, 0, 1);
        result.Score = Math.Round(score, 2);
        result.ContainsSpecialCategoryData = special;
        result.ContainsPersonalData = categories.Count > 0;
        result.CategoriesDetected = categories.OrderBy(x => x).ToList();
        result.Indicators = indicators.Take(25).ToList();
        result.ReviewReason = special
            ? "Se detectaron posibles categorías especiales de datos personales (LDP/LOPDGDD)."
            : null;
        result.Summary = BuildSummary(result);
        return result;
    }

    private static string BuildSummary(PersonalDataDetectionResult result)
    {
        if (!result.ContainsPersonalData)
            return "No se detectaron patrones de datos personales.";

        var baseSummary = $"Detectados datos personales. Categorías: {string.Join(", ", result.CategoriesDetected)}. Score: {result.Score:0.00}.";
        if (result.ContainsSpecialCategoryData)
            return $"{baseSummary} Revisión legal recomendada por posibles datos especialmente protegidos.";

        return baseSummary;
    }

    private static string CleanIndicator(string value)
    {
        var trimmed = Regex.Replace(value, @"\s+", " ").Trim();
        if (trimmed.Length <= 90)
            return trimmed;
        return trimmed[..90];
    }

    private static bool PassesLuhn(string digits)
    {
        var sum = 0;
        var alternate = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var n = digits[i] - '0';
            if (alternate)
            {
                n *= 2;
                if (n > 9)
                    n -= 9;
            }
            sum += n;
            alternate = !alternate;
        }
        return sum % 10 == 0;
    }
}
