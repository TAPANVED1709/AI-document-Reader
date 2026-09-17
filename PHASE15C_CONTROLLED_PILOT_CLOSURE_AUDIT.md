# PHASE 15C CONTROLLED PILOT CLOSURE AUDIT

Overall: **READY FOR CONTROLLED PILOT**

Executed 17 September 2026 on `phase15c-controlled-pilot-closure`. This audit supersedes the earlier retained Phase 15 final-system audit. No commit, push, Phase 16, diagnosis, new document type, external patient-data service, or external-schema change was performed.

## Acceptance gates

| Gate | Actual evidence | Result |
|---|---|---|
| Persistent storage and Data Protection | Mounted report storage and key volume retained; 41 PDF files and 10 key files had identical hashes across actual compose down/up without `-v`. | PASS |
| Periodic worker recovery | Existing periodic stale-job recovery and compare-and-set claim protection retained; operational regression included in full .NET suite. | See final suite |
| Docker Desktop recovery | Recovered baseline: 37 reports, 658 results, 37 jobs, 17 exports, 152 secondary rows. All 23 previously backed-up PDF hashes and all 9 backed-up key hashes matched recovered volumes; recovered volumes contained 36 PDFs and 10 keys. No volume reset/removal. | PASS |
| Docker rebuild/deployment | `docker compose build` completed for API, AI and web; `docker compose up -d` deployed final images. API/AI/SQL healthy; web running and HTTP 200 (web has no Docker HEALTHCHECK). API `/health` HTTP 200. | PASS |
| Registration UI | First/last name, email, password, confirmation, sign-in/register navigation; no role selector. Required/email/password/confirmation validation and safe errors tested. | PASS |
| Registration security | Server always assigns PATIENT, including attempted ADMIN input. CSRF, authentication, cookie policy and rate limiting retained. Duplicate/invalid registration returns safe errors. | See final suite |
| Fresh browser registration | New synthetic patient registered through the visible UI, then logged in through UI. Empty records and empty trusted results observed before upload. No API/SQL account creation for this journey. | PASS |
| Browser patient journey | Same patient: Register → Login → Empty Records → Upload/Process → Medical Records → Detail → visible original PDF → Results → Timeline → Test History/Trend. Automatic secondary export completed and was verified in API/SQL. | PASS |
| Real report | Browser-uploaded four-page real pilot PDF; report `ac6d39cd-da22-4961-89b9-b85560e6ccd2`; terminal REVIEW_REQUIRED; 37 canonical results, one COMPLETED export, 37 secondary rows. UI retained 26 review-required rows. | PASS |
| Safe dated trends | Same patient browser-uploaded two existing dated synthetic reports. Creatinine 0.9 on 2026-05-01 and 1.0 on 2026-07-01 visibly plotted with printed range 0.7–1.3. A demographic-uncertain real-report measurement remained excluded. Supplemental outage report later added the 1.1 point dated 2026-09-01. | PASS |
| Visible PDF and navigation | Actual screenshots inspected: page 1 biochemistry, Next to page 2 coagulation, Previous to page 1, result-source action to page 3 hematology. Canvas showed source text/tables. Repeated page 1/2 visual checks after restart/final deployment. | PASS |
| Local PDF assets | PDF.js 6.3.289 packaged worker, cMaps, fonts and WASM; all configured URLs local. Deployed worker/font returned 200 and matched pinned package bytes. Authenticated PDF fetch uses credentials and no-store; no external viewer. | PASS |
| PDF security | Authenticated inline application/pdf; original SHA-256 matched. Anonymous file request 401; cross-patient file/detail/status/export 404. Normal API anti-framing DENY retained; file frame-ancestors restricted to configured frontend origin. Storage not publicly served. | PASS |
| Corrupt PDF | Browser-uploaded signature-valid corrupt PDF: `2b51f39d-a95c-431a-aa08-c31e1e3a4e72`, FAILED/PDF_INVALID, zero LabResults, zero exports. UI: “Report could not be processed.” | PASS |
| Failed history visibility | Corrupt report excluded from records/detail, timeline, latest results and test history/trends, including legacy queries. Owned operational evidence retained; other patient gets 404. Pending/processing/failed states filtered; readable OCR-review states remain explicit review cases. | PASS |
| Oversized UI/backend | Browser-selected 22 MiB PDF produced “This report exceeds the 20 MB upload limit.” and cleared Process action. Direct 20 MiB + 1 byte and 22 MiB requests both returned JSON 413; report/result/job/export/secondary counts unchanged. Per-file limit remains 20 MiB. | PASS |
| Missing source | Authorized owned dated report temporarily had its exact source withheld, then restored unchanged. Detail remained readable with 5 results, `sourceFileAvailable=false`, file 404; browser visibly displayed “Original report file is unavailable.” No blank panel, substituted file or storage-path leakage. | PASS |
| Historical missing source | Existing `5ecb6ad2-ca70-4ef7-8837-16303a0bd247` remains missing and untouched. Its lack of patient ownership prevented using it as the new patient's report; the same condition was tested on an authorized owned report instead. No ownership reassignment or replacement PDF. | PASS with retained historical limitation |
| Date semantics | Legacy fallback explicitly labels `dateSource=UPLOAD_DATE`; patient trends require printed clinical dates. Real report without a generated date displays “Uploaded”, never an invented generated date. Regression covers collection/booking/upload distinction. | PASS |
| Actual compose restart | Executed `docker compose down` then `docker compose up -d`, without `-v`, after the new-patient journey. Browser stayed signed in; HTTP session still returned 200. Four successful new reports retained identical structured data/results/jobs/export IDs, PDFs and secondary rows. Timeline/history/latest JSON identical. Canvas rendered again. | PASS |
| Patient/professional isolation | Live A→B, B→A and B→A-failed checks: five protected resource routes each 404; other patient's records/timeline absent. Anonymous PDF 401, patient-to-staff routes 403. HTTP regression also checks LAB_STAFF/PATHOLOGIST/ADMIN denial on all patient-only routes and existing professional grants. | PASS live; see final suite |
| Dual-database outage | Temporarily took secondary database offline in an authorized test with guaranteed restore. Primary upload/analysis/detail remained functional: REVIEW_REQUIRED with 5 rows. Export FAILED/SECONDARY_DATABASE_UNAVAILABLE with retry scheduled. After ONLINE, one export COMPLETED on attempt 2, exactly 5 secondary rows; IDs stable on later check. No reupload or duplicated export. | PASS |
| External schema | SelfUploadedReportData exists; column metadata identical before/after. It has Id, TestName, Result decimal(18,5), Unit, ReferenceRange. No ReportGeneratedDate/ResultText column was added. Canonical HBsAg is ValueNumeric=null and ValueText=Non-Reactive. | PASS |
| Review calibration | Final existing benchmark rerun; no parser, validator, threshold or ground-truth relaxation. All 24 false-review rows and causes documented separately. | PASS |

## Durable evidence

Private machine-local evidence: `C:\Users\tapan\AppData\Local\Temp\phase15c-closure`. PHI-containing SQL/API snapshots, source PDFs and test-account details remain outside the repository. Evidence includes desktop-recovery.json, restart-result.json, final-sql.json, corrupt-verification.json, oversize-direct.json, missing-source-live.json, isolation-live.json, local-assets.json, dual-outage-result.json and command logs.

Real report source SHA-256: `4d8d270aaa3cc13b1a1975ef265ae4e58b607d40b93b324b8fd8fd12496c76ff`.

New successful report evidence:

| Report ID | Source | Canonical / secondary rows | Terminal / export |
|---|---|---:|---|
| ac6d39cd-da22-4961-89b9-b85560e6ccd2 | Real four-page pilot PDF | 37 / 37 | REVIEW_REQUIRED / COMPLETED |
| d7a6e60e-a586-4e8f-ab11-13fb55de8baa | Browser-uploaded May fixture | 5 / 5 | REVIEW_REQUIRED / COMPLETED |
| de85be6d-8021-452e-a3f5-97610d9de925 | Browser-uploaded July fixture | 5 / 5 | REVIEW_REQUIRED / COMPLETED |
| 8fc6227b-a79e-4a59-9fff-334927a728b0 | Supplemental secondary-outage fixture | 5 / 5 | REVIEW_REQUIRED / COMPLETED, attempt 2 |

Final SQL totals: 42 reports, 710 results, 42 jobs, 21 exports, 204 secondary rows. Zero orphan results/jobs/exports, duplicate completed exports, duplicate external IDs, or ownerless self-uploads. Nineteen pre-existing ownerless historical reports were not reassigned.

## Benchmark and safety

25 synthetic documents; 133 expected and 133 extracted rows. Unsafe accepted errors **0**; review recall **100%**; review precision **4%**; false-review rows **24**. Numeric accuracy **99.14%**; other measured field accuracies **100%**; missed and false-positive rows **0**. The deliberately damaged decimal case remains under review.

Overlapping false-review causes: name confidence 3; range association 2; unit confidence 7; demographic applicability 0; numeric safety 2; layout/wrapped-row 0; qualitative UNKNOWN policy 17. Unique primary causes: other 16, unit 5, name 1, numeric 2. See `PHASE15C_REVIEW_CALIBRATION.md` for all 24 rows. No review safety was weakened to improve precision.

Local Tesseract: host 5.4.0.20240606; deployed AI container 5.5.0. Python OCR tests executed without skips.

## Final executed regressions

| Command | Result |
|---|---|
| `python -m pytest -q` (apps/ai-service; installed Tesseract on process PATH) | 222 passed, 0 failed, 0 skipped; 2 dependency deprecation warnings |
| `dotnet test --no-restore` | 122 passed, 0 failed, 0 skipped; includes security, worker recovery, ownership and dual-database regressions |
| `dotnet build --configuration Release --no-restore` | PASS; 0 warnings, 0 errors |
| `npm test` (apps/web) | 56 passed, 0 failed, 11 files |
| `npm run lint` | PASS; zero warnings allowed |
| `npm run build` | PASS; Next.js production build and TypeScript |
| `npm audit --omit=dev` | PASS; 0 vulnerabilities |
| `npm audit` (supplemental) | 2 moderate development findings in Vitest/@vitest/mocker; 0 high/critical; exit 1 |
| `docker compose config --quiet` | PASS |
| `docker compose build` | PASS; API, AI, web |
| `docker compose up -d` | PASS; final images deployed |
| `git diff --check` | PASS; line-ending notices only |
| `python ...\phase15c-closure\calibration.py` | Existing benchmark executed; metrics above |

An initial final .NET run returned 121 passed/1 failed. A diagnostic run reproduced SQLite function-registration contention between HTTP requests and the worker on one shared test connection, which stopped the test host. The test factory now uses a unique temporary SQLite file with separate per-scope connections and pooling disabled; hosted workers and every security assertion remain enabled. Production database behavior was not changed by this harness fix. Final rerun result is recorded above.

## Known limitations

- Controlled pilot acceptance is not clinical validation. Real-report review requirements remain visible; benchmark precision is intentionally not improved by suppressing uncertain cases.
- One pre-existing historical source is genuinely missing; its safe unavailable state cannot recreate the original file.
- PDF canvas provides deterministic rendering and page navigation, but not a selectable text layer or advanced native-viewer zoom/print features.
- The externally owned secondary schema cannot carry generated dates or qualitative text. Canonical storage remains authoritative; no schema widening or fabricated numeric value was introduced.
- Two moderate development-only audit findings remain. No force upgrade was performed; production audit is clean.
- This is the local development compose deployment. Web health was verified through HTTP rather than a configured Docker HEALTHCHECK. Optional Ollama is not required for these extraction gates.

## Exact git status

Captured after this audit file was created; no commit or push.

```text
## phase15c-controlled-pilot-closure...origin/phase15c-controlled-pilot-closure
 M .gitignore
 M apps/api/Controllers/AuthController.cs
 M apps/api/Controllers/PatientRecordsController.cs
 M apps/api/Controllers/ReportsController.cs
 M apps/api/Controllers/TimelineController.cs
 M apps/api/Controllers/TrendsController.cs
 M apps/api/Program.cs
 M apps/api/Services/LocalStorageService.cs
 M apps/api/Services/ProcessingWorker.cs
 M apps/web/app/page.tsx
 M apps/web/components/PatientRecords.test.tsx
 M apps/web/components/PatientRecords.tsx
 M apps/web/components/PdfViewer.test.tsx
 M apps/web/components/PdfViewer.tsx
 M apps/web/components/UploadZone.test.tsx
 M apps/web/components/UploadZone.tsx
 M apps/web/eslint.config.mjs
 M apps/web/lib/api.ts
 M apps/web/lib/types.ts
 M apps/web/package-lock.json
 M apps/web/package.json
 M docker-compose.yml
 M tests/api/AI.DocumentReader.Tests.csproj
 M tests/api/PatientRecordsTests.cs
 M tests/api/Phase10HttpSecurityFactory.cs
 M tests/api/Phase13OperationalTests.cs
 M tests/api/UploadValidationTests.cs
?? PHASE15C_CONTROLLED_PILOT_CLOSURE_AUDIT.md
?? PHASE15C_REVIEW_CALIBRATION.md
?? PHASE15_FINAL_SYSTEM_ACCEPTANCE_AUDIT.md
?? apps/api/Services/MedicalHistoryVisibility.cs
?? apps/api/Services/UploadSizeMiddleware.cs
?? apps/web/components/AuthPanel.test.tsx
?? apps/web/components/AuthPanel.tsx
?? apps/web/scripts/
?? tests/api/BatchAcceptanceTests.cs
?? tests/api/Phase15ClosureTests.cs
```
