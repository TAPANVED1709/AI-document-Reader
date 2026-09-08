# AI Document Reader

Medical document intelligence application for pathology and laboratory reports.

> **Privacy-first.** All document processing runs locally on your machine. No patient data is transmitted to any external service.

---

## Features

| Feature | Stage | Status |
|---------|-------|--------|
| PDF upload & local storage | Stage 1 | ✅ |
| Text extraction from text-based PDFs | Stage 1 | ✅ |
| Lab result parsing (test name, value, unit, reference range) | Stage 1 | ✅ |
| Deterministic reference-range classification (NORMAL / LOW / HIGH / UNKNOWN) | Stage 1 | ✅ |
| SQLite local dev / SQL Server production DB | Stage 1 | ✅ |
| Local OCR for scanned/image-based PDFs (Tesseract) | Stage 2 | ✅ |
| Graceful fallback when OCR not installed | Stage 2 | ✅ |

---

## Architecture

```
AI-document-Reader/
├── apps/
│   ├── api/            # ASP.NET Core Web API (.NET 8) — port 5000
│   └── ai-service/     # Python FastAPI — port 8000
├── database/
│   └── scripts/        # SQL Server DDL (optional; SQLite used for local dev)
├── storage/
│   └── reports/        # Uploaded PDFs stored here locally
└── tests/
    └── api/            # xUnit tests for .NET API
```

### Data flow

```
Browser / Client
    │  POST /api/reports/upload (multipart PDF)
    ▼
ASP.NET Core API
    │  1. Validate & store PDF to storage/reports/{guid}.pdf
    │  2. POST /analyse (multipart PDF) to Python AI service
    ▼
Python FastAPI AI Service
    │  3. Extract text via PyMuPDF
    │  4a. Text PDF  ──────────────────────────────────────► LabRowParser
    │  4b. Scanned PDF + Tesseract available ── OCR ────────► LabRowParser
    │  4c. Scanned PDF + Tesseract missing  ── requiresOcr=true (returned as-is)
    ▼
ASP.NET Core API
    │  5. Apply deterministic reference-range classification (C#)
    │  6. Persist results to SQLite / SQL Server
    ▼
JSON Response  →  Browser / Client
```

---

## Prerequisites

### Required

| Tool | Minimum version | Install |
|------|----------------|---------|
| .NET SDK | 8.0 | https://dotnet.microsoft.com/download |
| Python | 3.11+ | https://python.org |

### Required for OCR support (Stage 2)

**Tesseract OCR** must be installed on the machine running the Python AI service.

#### Windows
```powershell
# Option 1: winget
winget install -e --id UB-Mannheim.TesseractOCR

# Option 2: Chocolatey
choco install tesseract

# After install, verify:
tesseract --version
```

> **Windows PATH note:** After installing, ensure the Tesseract install directory (e.g. `C:\Program Files\Tesseract-OCR`) is on your system `PATH`.

#### Linux (Debian / Ubuntu)
```bash
sudo apt-get update && sudo apt-get install -y tesseract-ocr tesseract-ocr-eng
```

#### macOS
```bash
brew install tesseract
```

> OCR is **optional**. If Tesseract is not installed, text-based PDFs process normally and scanned PDFs return `requiresOcr: true` with an empty results list. Install Tesseract and restart the AI service to enable automatic OCR.

---

## Local Development

### 1. Start the Python AI Service

```bash
cd apps/ai-service
pip install -r requirements.txt
python -m uvicorn main:app --host 0.0.0.0 --port 8000 --reload
```

Swagger UI: http://localhost:8000/docs

Health check: `GET http://localhost:8000/health`

Expected response (with Tesseract installed):
```json
{
  "status": "Healthy",
  "service": "ai-service",
  "version": "2.0.0",
  "ocrAvailable": true,
  "tesseractVersion": "5.x.x"
}
```

### 2. Start the .NET API

```bash
cd apps/api
dotnet run
```

Swagger UI: http://localhost:5000/swagger

---

## API Reference

### Upload a Report

```http
POST /api/reports/upload
Content-Type: multipart/form-data

file: <pdf file>
```

**Response (text-based PDF):**
```json
{
  "id": "3fa85f64-...",
  "status": "Completed",
  "requiresOcr": false,
  "ocrApplied": false,
  "resultsCount": 12,
  "results": [
    {
      "originalTestName": "Hemoglobin",
      "normalizedTestName": "Hemoglobin",
      "valueNumeric": 10.8,
      "valueText": "10.8",
      "unit": "g/dL",
      "referenceMin": 13.0,
      "referenceMax": 17.0,
      "referenceText": "13.0 - 17.0",
      "calculatedStatus": "LOW",
      "extractionConfidence": 0.95,
      "pageNumber": 1
    }
  ]
}
```

**Response (scanned PDF with OCR applied):**
```json
{
  "status": "Completed",
  "requiresOcr": true,
  "ocrRequired": true,
  "ocrApplied": true,
  "processingMode": "OCR",
  ...
}
```

**Response (scanned PDF, Tesseract not installed):**
```json
{
  "status": "RequiresOcr",
  "requiresOcr": true,
  "ocrApplied": false,
  "results": []
}
```

### Get a Report

```http
GET /api/reports/{id}
```

### Get Lab Results

```http
GET /api/reports/{id}/results
```

---

## Running Tests

### Python AI Service Tests

```bash
cd apps/ai-service
pytest tests/ -v
```

Tests requiring Tesseract are automatically **skipped** when Tesseract is not installed.

### .NET API Tests

```bash
dotnet test tests/api/AI.DocumentReader.Tests.csproj
```

---

## Docker (optional)

```bash
# Build and start all services
docker compose up --build

# Tesseract is automatically installed inside the ai-service container
```

---

## Medical Disclaimer

> This tool extracts and organises information from medical laboratory reports. It does **not** provide a medical diagnosis, clinical interpretation, or treatment recommendation. Always verify important results against the original source document and consult a qualified healthcare professional where appropriate.

---

## Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `ConnectionStrings__DefaultConnection` | SQLite | DB connection string (SQLite used if absent or contains "sqlite") |
| `AiService__BaseUrl` | `http://localhost:8000` | URL of Python AI service |
| `Storage__ReportsPath` | `storage/reports` | Directory for uploaded PDFs |

Copy `.env.example` to `.env` and adjust values for your environment.


## Stage 2 hardening contract

Extraction decisions are made per page. Meaningful native text is retained;
only insufficient pages are rendered at 300 DPI for local Tesseract OCR.
`ocrRequired` (also exposed as legacy `requiresOcr`) records whether any page
needed OCR, independently of `ocrApplied`. `processingMode` is `NATIVE`, `OCR`,
or `HYBRID` and describes the required strategy even when OCR is unavailable.
`pageSources` reports `NATIVE_TEXT`, `OCR`, `OCR_UNAVAILABLE`, or `OCR_FAILED`.
Successful engine execution with no recognized text still counts as OCR applied;
it does not imply a successful lab parse.

OCR uses `image_to_data` tokens with text, confidence (0–1), x/y/width/height,
page and Tesseract line identifiers. Reconstruction retains these tokens.
Result confidence is capped by the weakest token in the parsed name, numeric
value, unit, and reference range; scores below 0.8 are marked `lowConfidence`.
`boundingBoxJson` covers the relevant tokens in rendered pixels at 300 DPI,
with a one-based page number. No frontend highlighting is implemented.
Preprocessing uses grayscale and mild contrast, without sharpening or thresholding.
Pillow ImageFilter does not provide adaptive thresholding.

OCR state and bounding boxes persist across upload and report retrieval.
Startup adds missing state columns to existing SQLite/SQL Server tables without
removing data. Historical reports receive `processingMode=UNKNOWN`: their prior
OCR use cannot be reconstructed. The SQL Server upgrade path requires schema
alteration permission. SQLite upgrade is covered by an idempotence regression test.

Processing requires no external service: PyMuPDF, Pillow, Tesseract and the parser
run locally; classification is deterministic C#. The API communicates with the
Python service over configured HTTP (localhost by default; `ai-service` in Docker).
An operator can configure a remote URL or SQL Server, so local deployment remains
a configuration responsibility. Dependency installation downloads packages;
interactive Swagger documentation may fetch browser assets from a CDN.

This checkout has no frontend package or source directory. Frontend lint/build
cannot be certified here. See STAGE2_AUDIT.md for actual verification results.
