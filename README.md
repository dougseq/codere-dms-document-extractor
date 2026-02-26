# Azure Function: ExtractLicenseMetadata (.NET 8 isolated)

This function receives a PDF file **as Base64**, calls **Azure AI Document Intelligence (prebuilt-read)** to extract text,
then applies **regex + anchor proximity** rules to detect key metadata:
- Expediente
- Ayuntamiento, Municipio (optional hints accepted)
- Titular, NIF/CIF
- Dirección del local, Actividad
- Fechas: Concesión, Caducidad, Renovación
- Campos `GD_*`: `GD_AutoridadOrganismo`, `GD_FechaRevision`, `GD_FechaVigencia`, `GD_Pais`
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
  "GD_AutoridadOrganismo": "Ayuntamiento de Madrid",
  "GD_FechaRevision": "2025-01-15",
  "GD_FechaVigencia": "2026-01-15",
  "GD_Pais": "España",
  "confianzaExtraccion": 0.85,
  "motivoRevision": null,
  "palabrasClaveDetectadas": ["Caducidad: 15/01/2026"]
}
```

`GD_Pais` se limita a: `Argentina`, `Colombia`, `España`, `Italia`, `México`, `Panamá`, `Uruguay`.

### Campos `GD_*` (nuevo)
Estos campos se devuelven en el endpoint `POST /api/extract` para mapearlos directamente en columnas de SharePoint:

- `GD_AutoridadOrganismo` (`Single line of text`)
  - Valor extraído desde líneas con ancla `Autoridad` u `Organismo`.
  - Fallback: `Ayuntamiento` y, si no existe, `Municipio`.
- `GD_FechaRevision` (`Date only`)
  - Se rellena con la fecha detectada de `FechaRenovacion` (`yyyy-MM-dd`).
- `GD_FechaVigencia` (`Date only`)
  - Se rellena con la fecha detectada de `FechaCaducidad` (`yyyy-MM-dd`).
- `GD_Pais` (`Choice`)
  - Opciones válidas: `Argentina`, `Colombia`, `España`, `Italia`, `México`, `Panamá`, `Uruguay`.
  - Detección por presencia de país en el texto (comparación sin acentos).
  - Fallback actual: si se detecta `Ayuntamiento`, se asigna `España`.

### Configuración recomendada en SharePoint
- Crear `GD_AutoridadOrganismo` como `Single line of text`.
- Crear `GD_FechaRevision` como `Date and Time` con formato `Date only`.
- Crear `GD_FechaVigencia` como `Date and Time` con formato `Date only`.
- Crear `GD_Pais` como `Choice` con exactamente estas opciones:
  - `Argentina`
  - `Colombia`
  - `España`
  - `Italia`
  - `México`
  - `Panamá`
  - `Uruguay`

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

### Criterios de detección (implementación actual)
La detección se basa en reglas `regex` sobre el texto extraído. Si al menos una categoría coincide, el documento se marca como que contiene datos personales.

Categorías y señales principales:
- `Identificativo` (`+0.35`): patrones tipo DNI/NIE/CIF.
- `Contacto` (`+0.20`): email.
- `Contacto` (`+0.20`): teléfonos españoles (incluyendo prefijo `+34`).
- `Direcciones` (`+0.15`): términos como `domicilio`, `dirección`, `calle`, `avenida`, `plaza`, `c/` y texto cercano.
- `Financiero` (`+0.30`): IBAN español (`ES` + 22 caracteres alfanuméricos).
- `Especial` (`+0.40` a `+0.45`): salud, biométricos, ideología/opinión política/afiliación sindical, religión/creencias, orientación o vida sexual, origen racial/etnia, condenas o antecedentes penales.

Reglas adicionales:
- Tarjetas potenciales: secuencias de `13` a `19` dígitos (con separadores) se validan con algoritmo **Luhn**. Si pasa, se añade categoría `Financiero` (`+0.30`).
- Si se detectan `2` o más categorías, se añade un bonus de `+0.10`.
- `score` final: limitado a rango `0.00`-`1.00` y redondeado a `2` decimales.
- `containsPersonalData`: `true` cuando hay al menos una categoría detectada (no hay umbral mínimo de `score`).
- `containsSpecialCategoryData`: `true` cuando hay coincidencias de categoría `Especial`.
- `indicators`: se guardan muestras de coincidencias para trazabilidad (hasta `3` por regla y máximo `25` en total, limpiadas/acotadas).

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

## Endpoint: Resumen esquematizado de documentos
Endpoint local: `POST http://localhost:7071/api/summarize-document`

Esta función recibe un archivo en base64, extrae su texto y devuelve:
- `Sumario`: resumen consolidado.
- `PuntosClave`: texto con los puntos clave separados por `|`.
- `Tipo de Documento`: mejor coincidencia predicha para el campo Choice de SharePoint.
- `Autoridad Organismo`: valor estimado para columna `Single line of text`.
- `Fecha Revision`: valor estimado para columna `Date only`.
- `Fecha Vigencia`: valor estimado para columna `Date only`.
- `Pais`: valor estimado para columna `Choice`.

`Sumario` se construye concatenando: `heading + " " + structuredSummary`.

Formatos soportados:
- `.pdf`
- `.docx`
- `.pptx`
- `.xlsx`
- `.txt`

Notas de extracción:
- Para `.pdf`, `.docx`, `.pptx` y `.xlsx` se usa **Azure AI Document Intelligence**.
- Para `.txt` se decodifica texto directamente (UTF-8 y fallback Latin1).

### Request JSON
```json
{
  "fileName": "informe_trimestral.pptx",
  "contentBase64": "<BASE64-FILE>"
}
```

### Response JSON
```json
{
  "Sumario": "Resumen ejecutivo Documento: Informe Trimestral 2025 Tipo: .pptx ...",
  "PuntosClave": "El crecimiento de ingresos fue del 12% respecto al trimestre anterior. | Se abrieron dos nuevas lineas de negocio en el segmento enterprise.",
  "Tipo de Documento": "Informes de cumplimiento (compliance)",
  "Autoridad Organismo": "Ayuntamiento de Madrid",
  "Fecha Revision": "2025-01-15",
  "Fecha Vigencia": "2026-01-15",
  "Pais": "España"
}
```

Campos de respuesta:
- `Sumario`: texto final que combina encabezados y resumen estructurado.
- `PuntosClave`: cadena única con puntos clave detectados, normalizados en una sola línea y separados por `|`.
- `Tipo de Documento`: valor predicho dentro de las opciones configuradas del campo `Tipo de Documento` (Choice) en SharePoint.
- `Autoridad Organismo`: texto detectado/predicho desde el contenido (anclas como `Autoridad`, `Organismo`, `Ayuntamiento`, `Ministerio`, etc.).
- `Fecha Revision`: fecha detectada/predicha cerca de términos `revision`, `renovacion` o `actualizacion` (formato `yyyy-MM-dd`).
- `Fecha Vigencia`: fecha detectada/predicha cerca de términos `vigencia`, `caducidad`, `vencimiento` o `validez` (formato `yyyy-MM-dd`).
- `Pais`: valor detectado/predicho restringido a `Argentina`, `Colombia`, `España`, `Italia`, `México`, `Panamá`, `Uruguay`.

### Códigos de respuesta
- `200 OK`: análisis completado.
- `400 Bad Request`: JSON inválido, base64 inválido o extensión no soportada.
- `500 Internal Server Error`: error interno durante extracción o resumen.
