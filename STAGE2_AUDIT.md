# FINAL STAGE 2 COMPLETION AUDIT

Date: 2026-09-08. Scope: Stage 2 hardening only. No Stage 3 work or frontend redesign.

**Decision: backend hardening verified; full Stage 2 sign-off is withheld because this checkout contains no frontend package, so the requested frontend checks fail.**

## Actual command results

Commands ran in `C:\Users\tapan\OneDrive\Desktop\Projects\AI-document-Reader` unless noted.

| Command | Passed | Failed | Skipped | Actual outcome |
|---|---:|---:|---:|---|
| `../../.venv/Scripts/python.exe -X utf8 -m pytest tests -q` from `apps/ai-service`, with `C:/Program Files/Tesseract-OCR` prepended to PATH | 39 | 0 | 0 | Exit 0; 32.03 s; 2 dependency deprecation warnings |
| `dotnet test tests/api/AI.DocumentReader.Tests.csproj --logger 'console;verbosity=minimal'` | 30 | 0 | 0 | Exit 0; test duration 3 s |
| `dotnet build AI-document-Reader.sln --configuration Release` | N/A | N/A | N/A | Exit 0; 0 warnings, 0 errors; 59.90 s |
| `npm.cmd run lint` | N/A | N/A | N/A | Exit 1: ENOENT, package.json absent |
| `npm.cmd run build` | N/A | N/A | N/A | Exit 1: ENOENT, package.json absent |
| `git diff --check` | N/A | N/A | N/A | Exit 0; no whitespace errors |

The frontend commands failed before running any lint rules, tests, or compilation. There is no frontend source directory or package.json anywhere in the tracked project. Building a new frontend would violate the requested scope. These failures have not been relabeled as passing checks.

Environment: Python 3.12.14; .NET SDK 10.0.301 targeting net8.0; Node v24.14.1. Local OCR engine: **Tesseract v5.4.0.20240606**, Leptonica 1.84.1, pytesseract 0.3.13, Pillow 12.3.0, PyMuPDF 1.28.2. Installed a project-local ignored `.venv` and Tesseract to execute real OCR tests; no OCR tests were skipped in the final run.

Earlier failures were resolved: missing `mL/min` support; PSM 6 misreading HbA1c as HbAtc; a Windows event-loop socket in the network-denial test harness; missing FormFile headers in the new .NET fixture. PSM 4 recovered the exact printed HbA1c from the same synthetic image without a name-correction lookup. The final totals above are from reruns after those fixes.

## Hardened behavior

1. OCR uses `pytesseract.image_to_data`, retaining text, confidence normalized to 0–1, x, y, width, height, one-based page, and block/paragraph/line identifiers. Reconstruction groups original token objects into lines; it does not discard token metadata in favor of plain text.
2. Every PDF page is examined with native PyMuPDF extraction. Meaningful-text sufficiency is evaluated per page. Only insufficient pages are sent to OCR; the mixed-PDF test spies on the engine and verifies its only call is page 2.
3. `ocrRequired` and legacy alias `requiresOcr` retain the original requirement even after successful OCR. `ocrApplied` is independent. `processingMode` describes the extraction strategy; page sources expose unavailable and failed OCR explicitly. Native-page results survive unavailable/failed OCR on other pages.
4. Each OCR result uses the minimum token confidence for each matched field: name, numeric value, unit, reference range. Overall confidence cannot exceed the weakest field/token or parser confidence. Scores below 0.8 are flagged low-confidence. Six regression cases independently weaken name, value, unit and reference tokens to 0.21 and verify overall confidence stays 0.21.
5. BoundingBoxJson stores the union of relevant source tokens, page and coordinate-system metadata. Coordinates are in the 300-DPI rendered image, not PDF points. API upload, database persistence and retrieval retain the box and OCR state; SQLite round-trip tests cover this.
6. Rendering is 300 DPI, grayscale, mild 1.1 contrast. No sharpening or thresholding is performed. Pillow ImageFilter is not claimed to provide adaptive thresholding. Signed numeric values and <= inequalities have parser regression coverage.
7. An additive startup schema upgrade supports existing SQLite and SQL Server state columns. SQLite preservation/idempotence was executed in tests. No existing report data is deleted.

## Executed document examples

Full actual `/analyse` responses are saved in `STAGE2_RESULTS.json`.

| Input | ocrRequired | ocrApplied | processingMode | Page sources | Parsed rows |
|---|---|---|---|---|---:|
| Native text PDF | false | false | NATIVE | NATIVE_TEXT | 3 |
| Image-only scanned PDF | true | true | OCR | OCR | 3 |
| Native / scanned / native PDF | true | true | HYBRID | NATIVE_TEXT, OCR, NATIVE_TEXT | 9 |
| Scanned PDF with OCR unavailable | true | false | OCR | OCR_UNAVAILABLE | 0 |
| Mixed PDF with OCR unavailable | true | false | HYBRID | NATIVE_TEXT, OCR_UNAVAILABLE, NATIVE_TEXT | 6 |

The synthetic scans contain raster images only; tests verify their native text layer is empty. Each page contains the three specified laboratory rows. The mixed test verifies exact names, values, units and ranges on all three pages, not just non-empty output. Simulated engine failure also retains six native results and marks page 2 OCR_FAILED.

Scanned PDF actual parsed output:

| Test | Value | Unit | Reference range | Confidence | Low-confidence |
|---|---:|---|---|---:|---|
| Hemoglobin | 10.8 | g/dL | 13.0 - 17.0 | 0.89 | false |
| Creatinine | 0.9 | mg/dL | 0.7 - 1.3 | 0.89 | false |
| HbA1c | 6.8 | % | 4.0 - 5.6 | 0.75 | true |

HbA1c's recognized name had 0.75 confidence; the result remains low-confidence despite correct numeric recognition. Example Hemoglobin bounding box: x=213, y=368, width=982, height=64, page=1, rendered_pixels, dpi=300. Mixed scan boxes correctly use page=2.

## External service audit

No external HTTP/API service is required for OCR, document extraction, lab parsing or classification.

- PyMuPDF opens/render PDFs locally; Pillow processes local images; pytesseract executes the installed local Tesseract binary and language data. The parser is Python regex/token code. C# reference-range classification is deterministic local code.
- Runtime Python dependencies reviewed: FastAPI, uvicorn, PyMuPDF, Pydantic, python-multipart, pytesseract and Pillow. pytest/httpx are used for tests. No hosted OCR, LLM, medical parsing or classification SDK/call exists in these paths.
- The .NET API's HTTP calls are `/health` and `/analyse` to its configured Python service. Default appsettings use localhost:8000; the Docker example uses ai-service:8000. This is interprocess HTTP, not a required external service.
- EF Core supports local SQLite or configured SQL Server. Tests use in-memory SQLite and existing unit-test mocks. Operators can explicitly configure a remote Python URL or database; locality is not enforced against such configuration.
- A real mixed extraction/OCR/parsing test passes with Python socket.connect blocked. The test blocks sockets around the synchronous processing pipeline, avoiding Windows TestClient's internal loopback event-loop socket.
- Package installation and Docker image construction download dependencies. FastAPI's interactive Swagger UI may fetch CDN assets. Neither is an external processing dependency. No patient documents were used in verification.

## Known limitations

- Frontend lint/production build cannot pass without the absent frontend project; full Stage 2 completion remains unverified.
- Tests use clean synthetic English laboratory reports. Handwriting, skew, poor scans, complex multi-column layouts, split rows and unusual units are not certified. PSM 4 is a conservative single-column choice.
- Native-text sufficiency is heuristic (alphanumeric content plus meaningful alphabetic words). Pages with substantial native headers but scanned table content may evade OCR; region-level mixed content is not implemented.
- Confidence is conservative engine evidence, not a calibrated probability of correctness. Low-confidence results remain visible for review; classification uses extracted numeric bounds, including the pre-existing simplified handling of inequalities.
- OCR execution with empty output counts as applied, with zero results; it is not proof of successful parsing. OCR availability is probed at service startup, so install/PATH changes require restart.
- SQL Server schema upgrade was inspected and compiled but not integration-tested against a running SQL Server. Existing historic rows get processingMode UNKNOWN because their former OCR state cannot be recovered; boolean defaults are not historical evidence.
- Python emitted two dependency deprecation warnings for Starlette/httpx and AnyIO. Dependency ranges remain broad. Large-document performance/timeouts and advanced frontend highlighting are outside this pass.

## Exact git state

The following is a literal final `git status --porcelain=v1 --untracked-files=all` snapshot. No changes were staged or committed by this pass. Branch: main. HEAD: 8401c48560de1364d52f6aaa0fc70c5fc7e65e37.

```text
 M README.md
 M apps/ai-service/core/extractor.py
 M apps/ai-service/core/ocr_engine.py
 M apps/ai-service/main.py
 M apps/ai-service/parsers/base.py
 M apps/ai-service/parsers/lab_parser.py
 M apps/ai-service/tests/test_ocr_engine.py
 M apps/api/Controllers/ReportsController.cs
 M apps/api/Domain/MedicalReport.cs
 M apps/api/Program.cs
 M apps/api/Services/AiServiceClient.cs
?? STAGE2_AUDIT.md
?? STAGE2_RESULTS.json
?? apps/ai-service/tests/test_stage2_hardening.py
?? apps/api/Infrastructure/Stage2SchemaUpgrade.cs
?? tests/api/Stage2HardeningTests.cs
```
