# Azure Function: ExtractLicenseMetadata (.NET 8 isolated)

This function receives a PDF file **as Base64**, calls **Azure AI Document Intelligence (prebuilt-read)** to extract text,
then applies **regex + anchor proximity** rules to detect key metadata:
- Expediente
- Ayuntamiento, Municipio (optional hints accepted)
- Titular, NIF/CIF
- Dirección del local, Actividad
- Fechas: Concesión, Caducidad, Renovación
- ConfianzaExtraccion, MotivoRevision, Resumen

## Environment settings (local.settings.json)
- `DOCINTEL_ENDPOINT` = `https://<your-docintelligence>.cognitiveservices.azure.com/`
- `DOCINTEL_KEY` = API key
- `DEFAULT_LANGUAGE` = `es`

## Build & Run
```bash
dotnet build
func start
```
HTTP endpoint (local): `POST http://localhost:7071/api/extract`

### Example request body
```json
{
  "fileName": "licencia_123.pdf",
  "contentBase64": "<BASE64-PDF>",
  "AyuntamientoHint": "Ayuntamiento de Madrid",
  "MunicipalityHint": "Madrid"
}
```

### Example response
```json
{
  "expediente": "ABC-123/2024",
  "ayuntamiento": "Madrid",
  "fechaConcesion": "2024-01-15T00:00:00",
  "fechaCaducidad": "2026-01-15T00:00:00",
  "confianzaExtraccion": 0.85,
  "motivoRevision": null,
  "palabrasClaveDetectadas": ["Caducidad: 15/01/2026"]
}
```

> Tip: In **Power Automate**, send file content as base64 and update SharePoint columns with the response.

## Endpoint: Detección de datos personales (LDP/LOPDGDD)
Endpoint local: `POST http://localhost:7071/api/detect-personal-data`

Esta función recibe un archivo en base64 y determina si contiene datos personales según patrones comunes de cumplimiento (LDP/LOPDGDD), incluyendo posible detección de categorías especiales.

Formatos soportados:
- `.docx`
- `.pdf`
- `.xlsx`
- `.txt`

Notas de extracción:
- Para `.pdf`, `.docx` y `.xlsx` se usa **Azure AI Document Intelligence**.
- Para `.txt` se decodifica texto directamente (UTF-8 y fallback Latin1).

### Request JSON
```json
{
  "fileName": "expediente_123.pdf",
  "contentBase64": "<BASE64-FILE>"
}
```

Campos:
- `fileName`: nombre de archivo con extensión (`.pdf`, `.docx`, `.xlsx`, `.txt`).
- `contentBase64`: contenido del archivo codificado en base64.

### Response JSON
```json
{
  "fileType": ".pdf",
  "containsPersonalData": true,
  "containsSpecialCategoryData": false,
  "score": 0.65,
  "textLength": 4311,
  "categoriesDetected": ["Contacto", "Identificativo"],
  "indicators": ["12345678Z", "persona@dominio.es"],
  "reviewReason": null,
  "summary": "Detectados datos personales. Categorías: Contacto, Identificativo. Score: 0.65."
}
```

Campos de respuesta:
- `fileType`: extensión detectada.
- `containsPersonalData`: indica si se encontraron patrones de datos personales.
- `containsSpecialCategoryData`: indica posibles categorías especiales (p. ej. salud, biométricos, ideología, antecedentes penales).
- `score`: nivel de señal entre `0.00` y `1.00`.
- `textLength`: longitud del texto analizado.
- `categoriesDetected`: categorías detectadas (`Identificativo`, `Contacto`, `Direcciones`, `Financiero`, `Especial`).
- `indicators`: muestras de coincidencias detectadas (acotadas).
- `reviewReason`: motivo para revisión manual/legal cuando aplica.
- `summary`: resumen legible del resultado.

### Códigos de respuesta
- `200 OK`: análisis completado.
- `400 Bad Request`: JSON inválido, base64 inválido o extensión no soportada.
- `500 Internal Server Error`: error interno durante extracción o análisis.

### Ejemplo curl (PowerShell)
```powershell
$body = @{
  fileName = "expediente_123.pdf"
  contentBase64 = "<BASE64-FILE>"
} | ConvertTo-Json

Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:7071/api/detect-personal-data?code=<FUNCTION_KEY>" `
  -ContentType "application/json" `
  -Body $body
```
