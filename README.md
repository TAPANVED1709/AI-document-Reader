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
  "requiresOcr": false,
  "ocrApplied": true,
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
