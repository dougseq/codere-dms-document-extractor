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
