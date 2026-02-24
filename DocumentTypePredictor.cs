using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

public sealed class DocumentTypePredictor
{
    private sealed record Rule(string Choice, string[] StrongPhrases, string[] Keywords);

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "de", "del", "la", "el", "los", "las", "y", "o", "con", "sin", "para", "por", "en", "un", "una", "etc"
    };

    private static readonly Rule[] Rules =
    {
        new(
            "Contratos  finales",
            new[] { "contrato final", "contrato de servicios", "acuerdo comercial", "objeto del contrato" },
            new[] { "contrato", "clausula", "vigencia", "partes", "firmado" }),
        new(
            "Anexos y modificaciones contractuales",
            new[] { "anexo contractual", "modificacion contractual", "adenda", "enmienda contractual" },
            new[] { "anexo", "adenda", "modificacion", "enmienda", "prorroga" }),
        new(
            "Acuerdos de confidencialidad (NDA)",
            new[] { "acuerdo de confidencialidad", "non disclosure agreement", "informacion confidencial" },
            new[] { "nda", "confidencialidad", "secreto", "divulgacion", "confidencial" }),
        new(
            "Licencias y permisos",
            new[] { "licencia de actividad", "permiso de funcionamiento", "licencia de apertura" },
            new[] { "licencia", "permiso", "habilitacion", "concesion" }),
        new(
            "Documentos societarios",
            new[] { "registro mercantil", "junta general", "consejo de administracion", "capital social" },
            new[] { "estatutos", "societario", "accionistas", "administradores", "mercantil" }),
        new(
            "Autorizaciones oficiales",
            new[] { "autorizacion oficial", "resolucion administrativa", "organismo competente" },
            new[] { "autorizacion", "resolucion", "oficial", "expediente administrativo" }),
        new(
            "Certificados ISO, GDPR, LOPD, etc.",
            new[] { "certificado gdpr", "certificado rgpd", "certificado lopd", "iso 27701" },
            new[] { "gdpr", "rgpd", "lopd", "privacidad", "proteccion de datos", "iso" }),
        new(
            "Certificados de calidad, seguridad, medioambiente",
            new[] { "certificado de calidad", "certificado de seguridad", "certificado medioambiental", "iso 9001", "iso 14001", "iso 45001" },
            new[] { "calidad", "seguridad", "medioambiente", "ambiental", "iso", "prevencion" }),
        new(
            "Certificados de cumplimiento normativo o auditorías",
            new[] { "certificado de cumplimiento normativo", "auditoria de cumplimiento", "certificacion de cumplimiento" },
            new[] { "cumplimiento", "normativo", "auditoria", "certificado", "compliance" }),
        new(
            "Facturas oficiales",
            new[] { "numero de factura", "base imponible", "total factura" },
            new[] { "factura", "iva", "proveedor", "importe", "total", "vencimiento" }),
        new(
            "Declaraciones fiscales",
            new[] { "declaracion fiscal", "agencia tributaria", "modelo 303", "modelo 111" },
            new[] { "declaracion", "fiscal", "tributaria", "impuesto", "hacienda", "retenciones" }),
        new(
            "Informes de auditoría financiera",
            new[] { "auditoria financiera", "estados financieros", "opinion del auditor" },
            new[] { "balance", "cuenta de resultados", "auditor", "patrimonio", "financiera" }),
        new(
            "Comunicaciones con organismos públicos",
            new[] { "comunicacion oficial", "notificacion administrativa", "organismo publico" },
            new[] { "ministerio", "ayuntamiento", "notificacion", "organismo", "expediente", "administracion" }),
        new(
            "Informes de cumplimiento (compliance)",
            new[] { "informe de cumplimiento", "compliance report", "programa de cumplimiento" },
            new[] { "compliance", "cumplimiento", "riesgo", "canal etico", "normativo" }),
        new(
            "Evidencias de controles internos",
            new[] { "control interno", "matriz de controles", "evidencia de control" },
            new[] { "controles", "control", "evidencia", "trazabilidad", "segregacion" }),
        new(
            "Políticas y procedimientos corporativos",
            new[] { "politica corporativa", "procedimiento corporativo", "normativa interna" },
            new[] { "politica", "procedimiento", "corporativo", "codigo de conducta", "interna" }),
        new(
            "Manuales de operación",
            new[] { "manual de operacion", "guia operativa", "instrucciones operativas" },
            new[] { "manual", "operacion", "operativo", "instrucciones", "guia" }),
        new(
            "Procedimientos técnicos aprobados",
            new[] { "procedimiento tecnico", "especificacion tecnica", "aprobado por" },
            new[] { "tecnico", "procedimiento", "especificacion", "revision", "version", "aprobado" }),
        new(
            "Contratos laborales",
            new[] { "contrato de trabajo", "contrato laboral", "relacion laboral" },
            new[] { "empleado", "trabajador", "nomina", "jornada", "salario", "laboral" }),
        new(
            "Certificados de formación obligatoria",
            new[] { "certificado de formacion", "formacion obligatoria", "prevencion de riesgos" },
            new[] { "formacion", "certificado", "curso", "asistencia", "obligatoria" }),
        new(
            "Comunicaciones disciplinarias o legales.",
            new[] { "comunicacion disciplinaria", "apercibimiento", "sancion disciplinaria" },
            new[] { "disciplinaria", "sancion", "despido", "requerimiento legal", "demanda" }),
    };

    public string PredictBestMatch(string? fileName, string? sumario, string? puntosClave)
    {
        var normalizedText = Normalize($"{fileName} {sumario} {puntosClave}");
        if (string.IsNullOrWhiteSpace(normalizedText))
            return "Políticas y procedimientos corporativos";

        string bestChoice = "Políticas y procedimientos corporativos";
        var bestScore = double.MinValue;

        foreach (var rule in Rules)
        {
            var score = ScoreRule(rule, normalizedText);
            if (score > bestScore)
            {
                bestScore = score;
                bestChoice = rule.Choice;
            }
        }

        if (bestScore <= 0)
            return "Políticas y procedimientos corporativos";

        return bestChoice;
    }

    private static double ScoreRule(Rule rule, string normalizedText)
    {
        double score = 0;

        foreach (var phrase in rule.StrongPhrases)
        {
            if (Contains(normalizedText, phrase))
                score += 12;
        }

        foreach (var keyword in rule.Keywords)
        {
            if (Contains(normalizedText, keyword))
                score += 4;
        }

        var overlap = TokenOverlapScore(rule.Choice, normalizedText);
        score += overlap;

        if (rule.Choice.Equals("Acuerdos de confidencialidad (NDA)", StringComparison.OrdinalIgnoreCase) &&
            Contains(normalizedText, "nda"))
        {
            score += 8;
        }

        if (rule.Choice.Equals("Facturas oficiales", StringComparison.OrdinalIgnoreCase) &&
            Contains(normalizedText, "factura"))
        {
            score += 8;
        }

        if (rule.Choice.Equals("Contratos laborales", StringComparison.OrdinalIgnoreCase) &&
            (Contains(normalizedText, "laboral") || Contains(normalizedText, "trabajador")))
        {
            score += 8;
        }

        return score;
    }

    private static double TokenOverlapScore(string choice, string normalizedText)
    {
        var tokens = Normalize(choice)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length >= 4 && !Stopwords.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tokens.Count == 0)
            return 0;

        double score = 0;
        foreach (var token in tokens)
        {
            if (Contains(normalizedText, token))
                score += 1.25;
        }

        return score;
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(Normalize(needle), StringComparison.Ordinal);

    private static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var formD = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
