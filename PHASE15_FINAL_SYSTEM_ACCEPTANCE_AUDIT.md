# PHASE 15 FINAL SYSTEM ACCEPTANCE AUDIT

Overall: **NOT READY FOR CONTROLLED PILOT**

Executed 16–17 September 2026 (Asia/Calcutta), branch `audit/phase15-final-system-acceptance`, starting at commit `263bad5`. The initial working tree was clean. No commit, push, database contract change, UI feature, or Phase 16 implementation was performed.

The tested ingestion, extraction, primary persistence, export, authentication, and patient-history paths work for the controlled fixtures. The complete requested acceptance gate does not pass: browser registration is absent; a signature-valid corrupt PDF creates a failed medical-record entry; one pre-existing report has no stored PDF; and current native-PDF visual rendering/page navigation could not be verified in the automated browser. These are not hidden by the passing regression suites.

Private execution evidence, synthetic PDFs, SQL snapshots, benchmark outputs, and the repeatable audit harness are outside the repository at `C:\Users\tapan\AppData\Local\Temp\phase15-final-rp5ggn1u`. That directory contains synthetic account credentials and must not be committed or published wholesale. No real patient data is reproduced in this audit.

## 1. Environment and scope

| Item | Observed value |
|---|---|
| OS | Windows 11 Home Single Language, 10.0.26200 |
| CPU / RAM | Intel Core i5-13420H, 12 logical processors / 15.71 GiB |
| Docker / Compose | 29.7.2 / v5.5.1 |
| .NET SDK | 10.0.301; application targets net8.0 |
| Node / Python | v24.14.1 / 3.12.10 |
| Host Tesseract | 5.4.0.20240606, `C:\Program Files\Tesseract-OCR\tesseract.exe` |
| Container Tesseract | 5.5.0 |
| Frontend / API | localhost:3001 / localhost:5002 |
| SQL / AI | Container-network ports 1433 / 8000; no host-published ports |

Only these application/deployment defects were fixed:

1. Compose set `Storage__RootPath`, but `LocalStorageService` preferred the configured `Storage:ReportsPath`. PDFs therefore lived in `/app/storage/reports`, outside the mounted volume. Compose now sets `Storage__ReportsPath` to the mounted report directory. The 23 existing physical PDFs were backed up, copied without overwriting conflicting content, and hash-verified before recreation.
2. ASP.NET Data Protection keys were container-local. Compose now mounts a dedicated `api_keys` volume. Existing keys were backed up and migrated before the final recreation test. A live authenticated session survived `down/up`.
3. Worker stale-claim recovery ran only at startup. An immediate replacement could miss a claim that was not yet stale and never revisit it. Recovery now runs periodically; conditional updates check claim ownership, attempt and status, and skip the current worker's claims. A regression covers a claim becoming stale after replacement startup.

Audit coverage was also added for a mixed valid/invalid batch and extended to PATHOLOGIST denial on patient-only routes. No extraction, classification, correction, verification, or frontend implementation was changed.

## 2–3. Fresh registration, authentication and CSRF

Two new synthetic patients, A and B, were registered through `POST /api/auth/register` (201), logged in through the real API, and verified as PATIENT through `/api/auth/me` (200). No SQL user insertion was used. A third new account was created through the same registration API for the final browser journey.

Live checks:

| Request | Actual result |
|---|---|
| Anonymous protected patient endpoint | 401 |
| Authenticated upload, missing CSRF | 400 |
| Authenticated upload, invalid CSRF | 400 |
| Upload using anonymous token after login | 400 |
| Reacquire token for authenticated identity and upload | 202 |
| GET `/api/auth/csrf` | 200 |

The existing frontend token-lifecycle tests pass, including bounded retry and invalidation across login/logout. Cookie authentication and antiforgery remain enabled. Multipart upload uses FormData without manually supplying its Content-Type boundary.

## 4–5. Native, scanned and hybrid uploads

| Fixture / report ID | Upload | Mode and page sources | Terminal state | Primary / secondary rows |
|---|---|---|---|---|
| Native May report `411ea44e-00dd-47da-b0f8-f4cfa6fb611f` | 202 | NATIVE; page 1 NATIVE_TEXT | REVIEW_REQUIRED | 5 / 5 |
| Native July report `70c1eaaa-a89d-4d76-8ddf-e437910a3019` | 202 | NATIVE | REVIEW_REQUIRED | 5 / 5 |
| Native September report `8d1f0e70-46ab-463a-a29a-2d1b412c446e` | 202 | NATIVE | REVIEW_REQUIRED | 5 / 5 |
| Scanned `007b8953-a61f-45f9-bf29-ba4269b477d1` | 202 | OCR; required=true, applied=true | COMPLETED | 3 / 3 |
| Existing `pilot-06.pdf`, `616daa14-be07-4a1b-a5df-547b035b5131` | 202 | HYBRID; NATIVE_TEXT → OCR → NATIVE_TEXT | REVIEW_REQUIRED | 18 / 18 |
| Numeric/ambiguous-sex fixture `be858fd9-7732-421e-a193-31b596238cda` | 202 | NATIVE | REVIEW_REQUIRED | 4 / 4 |

The hybrid fixture already existed in the frozen pilot corpus. No new hybrid fixture was made for this audit. OCR used local Tesseract. The scanned report recovered Hemoglobin 10.8 g/dL, range 13.0–17.0; Creatinine 0.9 mg/dL, range 0.7–1.3; and HbA1c 6.8%, range 4.0–5.6.

The first generated scanned file was 25,248,570 bytes, above the existing 20 MiB limit, and was rejected at transport/request-size handling. The same image was losslessly PDF-compressed to 72,737 bytes before the successful OCR upload. No upload-size protection was relaxed.

## 6. Header extraction and date safety

Controlled source: `dated-0.pdf`. The final browser-uploaded instance is `4c804921-de95-47e8-8b94-0b4ca844e0fa`. Extraction was additionally checked with the current container's `/analyse` endpoint (200). Primary SQL means the canonical `MedicalReports.StructuredDataJson`, with explicit report date also stored as `ReportDate` and `ReportDateSource=DOCUMENT`. No manual corrections preceded measurement.

| Field | Source PDF | Extracted | Primary SQL | API | UI | Result |
|---|---|---|---|---|---|---|
| Patient Name | Synthetic Acceptance | Synthetic Acceptance | Synthetic Acceptance | Synthetic Acceptance | Synthetic Acceptance | MATCH |
| Patient ID | SYN-FINAL-15 | SYN-FINAL-15 | SYN-FINAL-15 | SYN-FINAL-15 | SYN-FINAL-15 | MATCH |
| Age | 22 Years | 22 / YEARS | 22 / YEARS | 22 / YEARS | 22 years | MATCH |
| Sex | Female | FEMALE | FEMALE | FEMALE | FEMALE | MATCH |
| DOB | 07-Mar-2004 | 2004-03-07 | 2004-03-07 | 2004-03-07 | 2004-03-07 | MATCH |
| Laboratory | Synthetic Acceptance Laboratory | Same | Same | Same | Same | MATCH |
| Report Number | R-FINAL-15 | R-FINAL-15 | R-FINAL-15 | R-FINAL-15 | R-FINAL-15 | MATCH |
| Booking Code | B-FINAL-15 | B-FINAL-15 | B-FINAL-15 | B-FINAL-15 | B-FINAL-15 | MATCH |
| Accession Number | A-FINAL-15 | A-FINAL-15 | A-FINAL-15 | A-FINAL-15 | A-FINAL-15 | MATCH |
| Report Generated Date | 01-May-2026 | 2026-05-01 | 2026-05-01 | 2026-05-01 | 2026-05-01 | MATCH |
| Collection Date | 15-Apr-2026 | 2026-04-15 | 2026-04-15 | 2026-04-15 | 2026-04-15 | MATCH |
| Collection Time | 08:20 | 08:20 | 08:20 | 08:20 | 08:20 | MATCH |
| Referring Doctor | Dr Example | Dr Example | Dr Example | Dr Example | Dr Example | MATCH |
| Specimen | Serum | Serum | Serum | Serum | Serum | MATCH |

14/14 required headers match after the documented date/case/unit normalization. Page count is 1 throughout. Booking date is separately preserved as 2026-04-14. The explicit generated date, 2026-05-01, differs from booking, collection, upload and analysis dates. The edge fixture has booking 2026-09-14 and collection 2026-09-15 but no generated date: `reportGeneratedDate` remains null. Neither upload nor analysis time fills it. The ambiguous source `Sex: Male or Female` produces nested patient sex=null.

## 7–10. Results, classification, applicability and numeric safety

The following source tuples were identical in extraction, primary SQL, secondary SQL, API and visible UI:

| Source test | Numeric result | Unit | Printed reference | Calculated status | Applicability | Review |
|---|---:|---|---|---|---|---|
| Hemoglobin | 10.8 | g/dL | 13.0 - 17.0 | LOW | NOT_REQUIRED | false |
| Creatinine | 0.9 | mg/dL | 0.7 - 1.3 | NORMAL | NOT_REQUIRED | false |
| HbA1c | 6.8 | % | 4.0 - 5.6 | HIGH | NOT_REQUIRED | false |
| ALT | 28 | U/L | 7 - 56 | NORMAL | NOT_REQUIRED | false |

Exact numeric-row statistics: names 4/4, values 4/4, units 4/4, ranges 4/4; 16/16 fields at each checked persistence/API stage, also visibly matched in the UI. The final report has five results, including the qualitative row. Its one review-required result is HBsAg; counts are 2 NORMAL, 1 HIGH, 1 LOW, 1 NEEDS REVIEW.

HBsAg source `Non-Reactive` persists as primary `ValueNumeric=NULL`, `ValueText=Non-Reactive`; API/UI preserve the text. Secondary `Result=NULL`, never zero. Under current classification rules it remains UNKNOWN/review-required.

Executed Python/.NET regressions cover printed one-sided bounds and dual-unit intervals. The demographic tests explicitly preserve mathematically NORMAL Creatinine against a male-specific printed range when patient sex is missing, while setting `ApplicabilityStatus=REVIEW_REQUIRED` and `ReviewRequired=true`. Conflicting demographic ranges and inequality values remain excluded from trusted plotting. These edge scenarios were automated tests, not all separate live uploads.

Live numeric safety:

| Candidate | Primary | Secondary | Safety result |
|---|---|---|---|
| Hemoglobin 108 (decimal-loss candidate) | 108 retained with original text; UNKNOWN/review | 108 retained | No invented 10.8; unsafe-to-trust measurement excluded from trends |
| Creatinine 6392437665405192000 | Numeric NULL; exact text retained; NUMERIC_OUT_OF_RANGE; review | NULL | No SQL range exception or row loss |
| Glucose 10000000000000 | Numeric retained within primary capacity; review | NULL | Secondary decimal(18,5) overflow suppressed; suppressed count=1 |
| HBsAg Non-Reactive | Numeric NULL, text retained | NULL | No fabricated number |

Primary extracted decimal capacity remains ±99,999,999,999,999.9999; corrected fields remain decimal(18,2); secondary capacity remains ±9,999,999,999,999.99999. Precision is checked as well as magnitude. Existing boundary/regression tests passed. No columns were widened; no human correction was overwritten.

## 11–14. Primary database, secondary contract, idempotency and outage

Canonical report, five LabResults and terminal ProcessingJob were inspected for the final browser report. Every sampled LabResult links to its correct MedicalReportId and authenticated patient owner. StructuredDataJson preserves the complete synthetic header; analysis/result data were read through SQL and API.

The secondary database is `MedicalSoftwareIntegrationDb`, table `dbo.SelfUploadedReportData`, with exactly the unchanged five columns: `Id bigint`, `TestName nvarchar`, `Result decimal(18,5) nullable`, `Unit nvarchar`, `ReferenceRange nvarchar`. It has no ReportGeneratedDate or ResultText column; those values cannot be persisted there under the current contract. Textual results remain canonical in the primary database. Primary export ledgers record external row IDs and ownership context.

Live secondary outage: the database was temporarily OFFLINE, and controlled report `6271c160-e177-4311-9d06-d1f76f208ab9` processed and retained five primary rows. Export entered FAILED with `SECONDARY_DATABASE_UNAVAILABLE`. The database was restored in a finally block before exhaustion. The same ledger completed on attempt 2 with five secondary rows and no re-upload. Automated tests also call completed exports again and race export workers; no duplicated delivery occurs. Ambiguous/lost COMMIT acknowledgement deliberately requires reconciliation rather than blind replay.

## 15–17. Worker, Docker and dependency recovery

Live AI service outage: same job went RETRYING/AI_SERVICE_UNAVAILABLE on attempt 1, then REVIEW_REQUIRED on attempt 2 after service restoration, with five rows and one export.

Live worker interruption: AI was paused to hold processing, the API worker was killed, AI was unpaused, and the API was restarted. Job `dc89c0c3-6d6b-47e0-8272-a992b66eaa96` for report `3181cb55-474c-4c9c-9383-9c8f8b1d2af4` recovered on attempt 2 to REVIEW_REQUIRED, with one logical job, five results and one export. This exercise used temporary test-only stale/retry timing overrides (10/15 seconds); default Compose configuration was restored afterward. Compose initially refused startup while a dependency health check was still recovering; after AI health recovered the API was explicitly started. The automated regression separately verifies a claim that is not yet stale at replacement startup. The synchronized concurrent claim test verifies one claim/result/validation-issue/analysis-run set.

Actual `docker compose down` followed by `docker compose up -d` was executed, without `-v`. Counts before and after: 36 reports, 653 results, 36 jobs, 16 export ledgers, 147 secondary rows. All 35 then-present PDF hashes were unchanged. An existing authenticated cookie remained valid and the source PDF returned 200 with identical bytes. No file or SQL restoration was performed between this down/up pair.

One older unowned report, `5ecb6ad2-ca70-4ef7-8837-16303a0bd247`, already lacked its PDF in the pre-audit physical-file backup and still lacks it. No source was guessed or substituted. This prevents claiming complete preservation of all historical originals. Restart persistence is proven; a full backup restore/disaster-recovery exercise is not.

Tesseract-unavailable behavior was tested using the existing supported mocks: native pages still parse, scanned pages return safe OCR_UNAVAILABLE/review behavior with no fabricated results. No OCR tests skipped in the Python run. Ollama was unavailable during live processing; a live explanation request returned 200 with deterministic fallback. No external OCR or AI API was introduced.

## 18. Invalid inputs and batch regression

Renamed non-PDF and invalid-signature files: 400. Empty upload: 400. Oversized PDF: transport ReadError/request-size rejection, not a clean HTTP error captured by this client; no report/export was created. That is a UX/acceptance limitation, not a successful 413 assertion.

Signature-valid corrupt PDF: upload 202, then terminal FAILED/PDF_INVALID at attempt 1, zero results and zero exports. A failed MedicalReports entry remains. Thus the requested no-phantom-history criterion FAILS, although no fabricated medical results are produced.

Added automated HTTP batch check: two existing synthetic valid PDF fixtures plus an invalid file return two distinct QUEUED jobs and one INVALID_PDF entry, preserving both valid reports. Invalid file creates no report. This checks real routing/auth/CSRF/storage/relational persistence in the test host; it does not claim a separate live staff batch processing run against Docker.

## 19–23. PDF, patient records, timeline, history and trends

Final browser journey verified empty dashboard, empty records, empty timeline and empty test history; then browser upload, processing, report detail, all header/result values, updated timeline and a trusted Creatinine 0.9 mg/dL point with its source link. No manual SQL changes occurred during this journey.

PDF response: 200, application/pdf, Content-Disposition inline, byte-for-byte equal to the source. The automated browser embeds the authenticated PDF URL but displays a blank native PDF panel. Current visible PDF rendering and page navigation are NOT VERIFIED by automation. Previous-phase user confirmation is not counted as a current audit pass.

Patient A's legitimately uploaded dated reports provide Creatinine 0.9 on 2026-05-01, 1.0 on 2026-07-01, and 1.1 on 2026-09-01. Timeline is chronological; the existing hybrid fixture stays undated. Recovery-test uploads add legitimate extra points with their own source IDs, rather than replacing the original series. Patient history uses stored data, not OCR reprocessing.

Current automated tests verify exclusion of unsafe numeric, unresolved review, NOT_APPLICABLE and qualified/inequality measurements; UI plotting excludes absent clinical dates and absent units. mg/dL and µmol/L are separate series without an unvalidated conversion. The older trend endpoint retains a labeled upload-date fallback, so it should not be mistaken for the stricter new patient chart's clinical-date contract.

## 24–27. Isolation, roles, audit safety and consistency

Live A→B report ID substitution returned 404 for report detail, file, patient record detail, export view and processing status. The legacy trend compare returned 200 with an empty points list: no data disclosure, but not the requested literal 403/404 response. Owner-scoped list/timeline/history/latest queries and forged-identity tests passed.

PATIENT, LAB_STAFF, PATHOLOGIST and ADMIN boundaries were exercised in automated HTTP tests, including denial of professional roles on patient-only routes and retained legitimate professional grants. Patient access does not expose stored filenames/filesystem paths in DTOs.

Inspected 60 representative security audit records: all MetadataJson values were null; events included login, PDF view, report queue/start/completion, processing view and export-status view. No passwords, cookies, tokens, PDF/OCR dumps, result dumps or connection strings were present in those records. This is a representative structured-audit check, not a claim that every operational log was exhaustively scrubbed.

Final SQL counts after the final journey:

| Check | Count |
|---|---:|
| MedicalReports / LabResults / ProcessingJobs | 37 / 658 / 37 |
| Export ledgers / secondary rows | 17 / 152 |
| Orphan LabResults | 0 |
| Orphan ProcessingJobs | 0 |
| Orphan exports | 0 |
| Duplicate completed export ledgers | 0 |
| Duplicate external IDs within an export ledger | 0 |
| Patient-origin uploads without patient ownership | 0 |
| All reports lacking patient ownership, including legacy/staff records | 19 |
| Existing report originals missing from storage | 1 |

The five-column external table has no report ownership or idempotency key. Attribution/deduplication checks therefore use the primary export ledger, not invented external columns.

`.env` is ignored and not tracked. Current tracked configuration uses password placeholders; verification scripts generate synthetic credentials at runtime. SQL/secondary SQL and AI have no host-published ports. API/web are published on host interfaces; this is a local HTTP deployment, not proof of production TLS or network hardening.

## 28. Performance and benchmark

Existing `validation.run_benchmark.run()` was executed against current code with output redirected to the private evidence directory, leaving historical benchmark files untouched. 25 documents; 133 expected and extracted rows; zero missed rows, false-positive rows or wrong associations. One numeric mismatch, 108 versus expected 10.8 in the deliberate decimal-loss sample, remains review-required. No unsafe accepted errors.

Accuracy: document type 100%, names 100%, numeric 99.14%, text values 100%, units 100%, reference ranges 100%, flags 100%, sections 100%, rows 100%, pages 100%, methods 100%. Review recall 100%, review precision 4%, false-review rate 18.18%, safe failure 100%. There were 24 false review flags; this is a material calibration/workload limitation.

Single warm local API measurements with Patient A's then-current six reports:

| Endpoint | Response | Returned count | Time |
|---|---|---:|---:|
| medical-records | 200 | 6 reports | 64.39 ms |
| medical-timeline | 200 | 6 reports | 21.85 ms |
| latest-results | 200 | 19 measurements | 35.06 ms |
| test-history/Creatinine | 200 | 5 measurements | 31.15 ms |

These are local point measurements, not throughput, percentile latency, load capacity or production sizing results. Concurrent unrelated host workloads affected early regression timing.

## 29–30. Dependencies, regressions and builds

| Command | Working directory | Actual final result |
|---|---|---|
| `python -m pytest -q` | apps/ai-service; Tesseract directory prepended to process PATH | 222 passed, 0 failed, 0 skipped; 2 deprecation warnings; 360.85 s |
| `dotnet test --no-restore` | repository root | 117 passed, 0 failed, 0 skipped; 1 m 13 s |
| `dotnet build --configuration Release --no-restore` | repository root | PASS; 0 warnings, 0 errors |
| `npm test` | apps/web | 48 passed, 0 failed, 10 files; 26.97 s |
| `npm run lint` | apps/web | PASS, exit 0 |
| `npm run build` | apps/web | PASS; optimized Next.js build and TypeScript completed |
| `npm audit --omit=dev` | apps/web | PASS; 0 vulnerabilities |
| `docker compose config --quiet` | repository root | PASS |
| `docker compose build` | repository root | PASS; API, AI and web images built |
| `git diff --check` | repository root | PASS; only Git LF/CRLF conversion notices |

Total final automated tests: 387 passed, zero failed/skipped. The first frontend run under concurrent build/test load had 45 passed and 3 five-second timeouts; rerunning unchanged frontend tests after those builds finished passed 48/48. Full development dependency audit was not run, and no claim is made about dev-only vulnerability counts.

## 31. Fresh-user final journey

Synthetic account `final15-journey-2f82a46a55@synthetic-pilot.test` registered through the real API (201/PATIENT). Browser then performed login → empty dashboard/records/timeline/trends → PDF selection → Process report → persisted five-result report → full header/results → timeline → trusted test-history point. Read-only SQL/API checks confirmed secondary export COMPLETED with five rows. Original PDF bytes were unchanged.

Strict uninterrupted browser Register→PDF-visible journey: **NOT PASSED**. There is no browser registration form, and the automated native PDF panel did not render visibly. Neither was disguised as a pass. No UI feature was added, and no manual SQL mutation was used during the journey.

## 32. Readiness matrix

| Subsystem | Classification | Qualification |
|---|---|---|
| Document ingestion | NOT READY | Corrupt-file history and oversized error UX remain |
| OCR | PILOT READY | Local native/scanned/hybrid and unavailable tests passed |
| Pathology extraction | PILOT READY | Synthetic benchmark only; review remains required |
| Header extraction | PILOT READY | Controlled 14/14 matrix; date/sex ambiguity safeguards |
| Classification | PILOT READY | Deterministic numeric/applicability separation verified |
| Review workflow | PILOT READY | Existing safety/role tests; high false-review burden |
| Primary persistence | PILOT READY | Live numeric isolation, ownership, consistency passed |
| Secondary export | PILOT READY | Outage/idempotency passed; legacy text/date and reconciliation limits |
| Patient ownership/security | PILOT READY | New self-upload isolation passed; legacy ownerless rows remain |
| Patient medical records | NOT READY | Original PDF visual gate and one historical missing PDF unresolved |
| Timeline | PILOT READY | Dated/undated behavior verified |
| Trends | PILOT READY | New patient chart safeguards; old endpoint date convention differs |
| Backup/recovery | NOT READY | Restart persistence fixed/proven; missing original and no full restore drill |
| Deployment | PILOT READY | Current local Docker stack; production TLS/capacity not established |
| Clinical validation | NOT READY | Engineering/synthetic tests are not clinical validation |

## 33–34. Remaining limitations and recommended Phase 16

Close acceptance blockers before starting new phase features: agree how fresh patients are provisioned or explicitly authorize browser registration; decide and verify corrupt-upload history handling; restore/reconcile the missing original without guessing; obtain current visible PDF/page-navigation evidence; and improve oversized-upload error handling. Retain explicit distinction between safe legacy trend responses and the new patient chart, and between primary evidence and the restricted secondary contract.

Recommended subsequent work, not implemented: supervised prospective clinical validation with real-layout coverage, review-calibration measurement, a tested encrypted backup/restore procedure, TLS/deployment validation, and a reconciliation runbook for indeterminate secondary commits. No production or clinical capacity conclusion follows from this audit.

## 35. Exact git status

```text
## audit/phase15-final-system-acceptance...origin/audit/phase15-final-system-acceptance
 M apps/api/Services/ProcessingWorker.cs
 M docker-compose.yml
 M tests/api/AI.DocumentReader.Tests.csproj
 M tests/api/PatientRecordsTests.cs
 M tests/api/Phase13OperationalTests.cs
?? PHASE15_FINAL_SYSTEM_ACCEPTANCE_AUDIT.md
?? tests/api/BatchAcceptanceTests.cs
```

No commit or push was made. All stopped/paused services and the secondary database were restored; temporary recovery configuration was removed by returning to default Compose before the final journey.
