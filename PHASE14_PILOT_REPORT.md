READY FOR CONTROLLED LAB PILOT

# FINAL PHASE 14 PATHOLOGY PILOT AUDIT

Executed 2026-09-09T18:36:42.312619+00:00. Branch `phase14-pathology-pilot`. This is acceptance for a supervised shadow pilot, not autonomous clinical use. **31 synthetic PDFs, 50 pages, 446 expected rows, 446 persisted rows.**

## Executed architecture and corpus

Authenticated LAB_STAFF HTTP ingestion -> persisted ProcessingJob -> worker -> local AI -> native/Tesseract -> parser -> validation -> SQL -> reports API. Each upload used a real synthetic LAB_STAFF login, CSRF token, lab organization, authorized patient association and idempotency key. No production security checks were disabled. A uniquely named Docker project isolated each run. SQL/API/AI processing was real; comparator input was the reports API's persisted machine output, not parser-only output.

OCR engine: **tesseract 5.5.0**. Native pages bypassed OCR; scanned pages used local 300-DPI Tesseract. Input scans include 200-DPI pages and deliberately degraded 135-DPI pages. All per-page sources and processing modes matched truth. Required case styles include single glucose/method reports, multipage biochemistry, multisection reports, three-column tables, parallel panels, wrapped names/ranges, close rows, repeated methods, qualitative values, demographic intervals, missing fields, comments, printed flags and unusual values.

Truth was authored independently of application output and frozen with PDF/JSON hashes. The 31-document baseline and final run use exactly the same manifest. The user clarified that reference reports contained basic results and printed ranges; no real source PDF was available. Every fixture is synthetic, without names, DOB, address, accession/patient numbers or other real identifiers. Ages/sex are invented parser inputs. All 50 pages were visually checked; enlarged Poppler renders confirmed readable layouts and deliberate scan damage.

## Overall results (percentages)

| Metric | Baseline | Final |
|---|---|---|
| Document classification | 96.77% | 100.00% |
| Original test name | 95.07% | 98.65% |
| Normalized test name | 95.74% | 99.33% |
| Numeric value (425 numeric rows) | 95.06% | 98.82% |
| Qualitative value (21 textual rows) | 100.00% | 100.00% |
| Original + normalized unit | 95.07% | 98.65% |
| Complete printed reference | 86.77% | 98.65% |
| Printed flag | 95.74% | 99.33% |
| Calculated status | 88.56% | 98.65% |
| Section | 95.07% | 100.00% |
| Page | 96.41% | 100.00% |
| Complete row association | 84.53% | 97.76% |
| Method | 96.41% | 100.00% |
| ReviewRequired | 89.01% | 93.72% |
| ReviewState | 89.01% | 93.72% |


Numbers use exact Decimal equality. No numeric tolerance is used. Null/omitted API properties both represent absence; unexpected non-null fields fail. Whitespace/dash typography and spacing around range separators/operators are the only scoring equivalences. Raw text is exported unchanged. Row association is the strict name/value/unit/range/page/section/method bundle, not a claim of pixel-perfect geometry. Missing rows remain in accuracy denominators; extra and missing rows never cancel each other.

## Review and safety

| Count | Baseline | Final |
|---|---|---|
| falsePositives | 53 | 0 |
| missedRows | 16 | 0 |
| wrongAssociations | 65 | 6 |
| wrongValueAssociations | 0 | 0 |
| wrongRangeAssociations | 0 | 0 |
| wrongUnitAssociations | 0 | 0 |
| wrongPageSectionAssociations | 6 | 0 |
| TrueReviewPositive | 73 | 16 |
| FalseReviewPositive | 21 | 23 |
| FalseReviewNegative | 54 | 0 |
| TrueReviewNegative | 351 | 407 |
| UnsafeAcceptedErrors | 38 | 0 |
| UnsafeAcceptedNumericErrors | 0 | 0 |
| UnsafeAcceptedAssociationErrors | 6 | 0 |
| UnsafeAcceptedUnitRangeErrors | 32 | 0 |


| Metric | Baseline | Final |
|---|---|---|
| ReviewRecall | 57.48% | 100.00% |
| ReviewPrecision | 77.66% | 41.03% |
| FalseReviewRate | 5.65% | 5.35% |
| SafeFailureRate | 57.48% | 100.00% |


Incorrect/source-ambiguous rows are review positives. Correct rows sent to review are false positives. Missed rows count as review false negatives; fabricated auto-accepted rows count as unsafe. A safe route requires both reviewRequired=true and REVIEW_REQUIRED state. The final six name/identity discrepancies are reviewed OCR names; no detectable cross-row value, unit, range or page/section swaps remain. A match to another row is a possible association signal, not proof of causal provenance. Zero observed unsafe errors is an internal engineering target, not a population safety guarantee.

## Per-format results

| Group | Docs | Expected | Extracted | Numeric | Unit | Range | Row association | FP | Missed | Unsafe |
|---|---|---|---|---|---|---|---|---|---|---|
| Native | 16 | 237 | 237 | 100.00% | 100.00% | 100.00% | 100.00% | 0 | 0 | 0 |
| Scanned | 11 | 147 | 147 | 96.27% | 95.92% | 95.92% | 93.20% | 0 | 0 | 0 |
| Hybrid | 4 | 62 | 62 | 100.00% | 100.00% | 100.00% | 100.00% | 0 | 0 | 0 |
| Multi-page | 12 | 207 | 207 | 100.00% | 99.52% | 99.52% | 97.58% | 0 | 0 | 0 |
| Multi-column | 5 | 70 | 70 | 100.00% | 100.00% | 100.00% | 100.00% | 0 | 0 | 0 |
| Low-quality OCR | 3 | 42 | 42 | 87.80% | 88.10% | 88.10% | 83.33% | 0 | 0 | 0 |


## Per-panel results

| Group | Docs | Expected | Extracted | Numeric | Unit | Range | Row association | FP | Missed | Unsafe |
|---|---|---|---|---|---|---|---|---|---|---|
| CBC | 16 | 181 | 181 | 99.45% | 99.45% | 99.45% | 99.45% | 0 | 0 | 0 |
| Liver | 10 | 80 | 80 | 100.00% | 100.00% | 100.00% | 97.50% | 0 | 0 | 0 |
| Kidney | 17 | 65 | 65 | 96.92% | 96.92% | 96.92% | 96.92% | 0 | 0 | 0 |
| Diabetes | 11 | 20 | 20 | 90.00% | 90.00% | 90.00% | 90.00% | 0 | 0 | 0 |
| Lipids | 3 | 12 | 12 | 100.00% | 100.00% | 100.00% | 100.00% | 0 | 0 | 0 |
| Thyroid | 2 | 4 | 4 | 100.00% | 100.00% | 100.00% | 100.00% | 0 | 0 | 0 |
| Iron | 3 | 9 | 9 | 100.00% | 100.00% | 100.00% | 88.89% | 0 | 0 | 0 |
| Vitamins | 3 | 9 | 9 | 100.00% | 100.00% | 100.00% | 100.00% | 0 | 0 | 0 |
| Inflammation | 3 | 6 | 6 | 100.00% | 83.33% | 83.33% | 83.33% | 0 | 0 | 0 |
| Coagulation | 5 | 15 | 15 | 100.00% | 100.00% | 100.00% | 100.00% | 0 | 0 | 0 |
| Urine | 4 | 13 | 13 | N/A | 100.00% | 100.00% | 100.00% | 0 | 0 | 0 |
| Electrolytes | 5 | 20 | 20 | 100.00% | 100.00% | 100.00% | 95.00% | 0 | 0 | 0 |
| Serology / Immunology | 2 | 8 | 8 | N/A | 100.00% | 100.00% | 100.00% | 0 | 0 | 0 |
| Other | 2 | 4 | 4 | 100.00% | 100.00% | 100.00% | 100.00% | 0 | 0 | 0 |


Format groups overlap: Multi-page/Multi-column/Low-quality OCR are additional tags. Panel groups follow authored rows; unmatched predictions would be attributed to Other. N/A means no applicable denominator, never an assumed 100%. Full subgroup metrics and denominators are in JSON.

## Document summaries

{'PASS': 13, 'REVIEW': 13, 'FAIL': 5}. These are technical verdicts. FAIL includes reviewed identity/association errors and does not imply a clinical severity category.

| Document | Description | Expected / extracted | Verdict |
|---|---|---|---|
| pilot-01 | Single glucose / hexokinase | 1 / 1 | PASS |
| pilot-02 | Single scanned glucose | 1 / 1 | PASS |
| pilot-03 | CBC three-column table | 12 / 12 | PASS |
| pilot-04 | Scanned CBC three-column table | 12 / 12 | REVIEW |
| pilot-05 | Biochemistry continuation | 18 / 18 | REVIEW |
| pilot-06 | Hybrid biochemistry continuation | 18 / 18 | REVIEW |
| pilot-07 | Scanned biochemistry | 16 / 16 | FAIL |
| pilot-08 | Multi-section metabolic and coagulation | 19 / 19 | PASS |
| pilot-09 | Wrapped names and ranges | 20 / 20 | PASS |
| pilot-10 | Scanned wrapped rows | 16 / 16 | REVIEW |
| pilot-11 | Parallel biochemical panels | 16 / 16 | REVIEW |
| pilot-12 | Parallel hematology and lipid panels | 16 / 16 | REVIEW |
| pilot-13 | Female printed intervals | 16 / 16 | PASS |
| pilot-14 | Male printed intervals | 16 / 16 | REVIEW |
| pilot-15 | Sex unknown printed intervals | 16 / 16 | REVIEW |
| pilot-16 | Conflicting sex metadata | 16 / 16 | REVIEW |
| pilot-17 | Child printed age intervals | 16 / 16 | PASS |
| pilot-18 | Infant printed month intervals | 16 / 16 | REVIEW |
| pilot-19 | Age unknown printed intervals | 16 / 16 | REVIEW |
| pilot-20 | Qualitative urine and serology | 11 / 11 | PASS |
| pilot-21 | Scanned qualitative values | 11 / 11 | PASS |
| pilot-22 | Endocrine and nutrition | 14 / 14 | PASS |
| pilot-23 | Hybrid endocrine and nutrition | 14 / 14 | PASS |
| pilot-24 | Missing fields retained for review | 14 / 14 | REVIEW |
| pilot-25 | Comments and close distinct rows | 17 / 17 | PASS |
| pilot-26 | Repeated concepts with methods | 15 / 15 | PASS |
| pilot-27 | Faint scanned decimals | 14 / 14 | FAIL |
| pilot-28 | Low resolution scan and character corruption | 24 / 24 | FAIL |
| pilot-29 | Hybrid flags and unusual printed values | 14 / 14 | REVIEW |
| pilot-30 | Scanned multipanel continuation | 17 / 17 | FAIL |
| pilot-31 | Decimal loss triplet and absent qualitative control | 4 / 4 | FAIL |


## Remaining extraction errors

| Document/row | Expected | Machine numeric | Reviewed |
|---|---|---|---|
| pilot-27-r02 | 6.8 | 6.0 | True |
| pilot-27-r03 | 0.9 | 0.0 | True |
| pilot-31-r01 | 10.8 | 10.0 | True |
| pilot-31-r02 | 6.8 | 6.0 | True |
| pilot-31-r03 | 0.9 | 0.0 | True |


Faded decimal cases deliberately retain clean authored values in truth. Tesseract's actual mistakes include splitting/truncating the value rather than always concatenating digits; no 108/68/09 outcome is fabricated. All such numeric errors are reviewed, and unusual clear values (including sodium 167 and ionized calcium 1.9) remain uncorrected by the machine. Every name/unit/range/flag/review mismatch is listed in PHASE14_PILOT_ERRORS.csv and full expected/actual rows in PHASE14_ROW_COMPARISON.csv.

Field-level engineering severity counts: {"A": 0, "B": 9, "C": 42, "D": 0, "E": 75}. A = wrong numeric auto-accepted; B = identity/association; C = unit/reference; D = missed row; E = other metadata/review discrepancy or numeric error already reviewed. One result can generate several field errors. Comment false positives: 0.

## Printed ranges, methods and human workflow

Female, male, child-year and infant-month intervals select only the explicitly printed matching range. Missing/conflicting demographic metadata preserves the complete printed text and requires review. The final section/page/method scores are 100%; repeated glucose methods remain separate. Comments (sample hemolysed, repeat advised, fasting sample, reference range revised) do not become result rows.

The authenticated review queue, numeric correction, correction-audit read, verification endpoint and secure PDF read were executed. The correction changed a synthetic HbA1c machine value from 6.0 to 6.8, preserving the extracted value and audit record. Baseline machine output was saved before correction and used for all scores. This exercise verifies endpoint/state transitions; it is not a full clinical adjudication of the corrected row. The current correction contract cannot edit every range-type/method/section field, so a real reviewer must resolve/escalate remaining discrepancies before signing off. See execution.workflowChecks for responses.

## Performance observations

| Duration (ms) | Minimum | Mean | Maximum |
|---|---|---|---|
| queueWaitMs | 52 | 1377.52 | 2010 |
| processingDurationMs | 28 | 1336.68 | 6061 |
| totalDurationMs | 104 | 2714.19 | 8071 |


OCR duration was not separately instrumented. These local timings include resource contention and are not a performance certification or a controlled before/after speed comparison.

## Executed regression checks

| Command | Result |
|---|---|
| python -m pytest -q | 140 passed, 0 failed |
| dotnet test | 57 passed, 0 failed |
| dotnet build --configuration Release | exit 0 |
| npm test | 17 passed, 0 failed |
| npm run lint | exit 0 |
| npm run build | exit 0 |
| npm audit --json | {"info": 0, "low": 0, "moderate": 2, "high": 0, "critical": 0, "total": 2} |
| npm audit --omit=dev --json | {"info": 0, "low": 0, "moderate": 0, "high": 0, "critical": 0, "total": 0} |
| docker compose config --quiet | exit 0 |
| docker compose build | exit 0 |
| git diff --check | exit 0 |


Python: no skips; two Starlette/httpx deprecation warnings. .NET Release: zero warnings/errors. Full npm audit: two moderate dev findings (vitest/@vitest/mocker, GHSA-82fw-gwwq-j7x9), zero high/critical. Production audit: zero vulnerabilities. No dependency upgrade was introduced in this pilot phase. Initial failing regression runs and their fixes are retained in tests/pilot/DEFECT_LOG.md and runs/baseline.

## Limitations and external-service audit

This corpus has 31 synthetic English reports and repeated analytes/templates. It was authored/reviewed by the development agent, not independently adjudicated by qualified laboratory reviewers. It does not estimate real-lab population accuracy, handwriting performance, scanner/vendor diversity or clinical suitability. The six name discrepancies, five numeric errors and review workload remain explicit. Only two parallel panels are reconstructed; more complex columns/rotations and incomplete source information require further evaluation. Matching uses a documented heuristic and approximate source positions; the full row exports support human checking. Printed reference storage remains bounded to 100 characters; an overlong unsupported result now fails safely rather than stranding a job. Correction of every metadata field is not implemented. A fresh holdout and qualified double review are required before any real-lab expansion.

Local PyMuPDF, Tesseract, deterministic parser/validator; no external medical/OCR API. Real ingestion uses loopback API and internal Docker AI/SQL; optional Ollama was not needed. The regression suite includes native/scanned processing with socket connections forbidden. No new cloud medical/OCR API, external patient-data transfer, real identifiers or production security bypass was introduced. The fixture-only ReportLab dependency is separate from production requirements.

## Protocol and artifacts

[Real-lab pilot protocol](docs/PATHOLOGY_LAB_PILOT_PROTOCOL.md) covers lab/sample selection, de-identification, qualified reviewers, independent truth, double review/adjudication, template/scanner balance, error metrics, engineering release gate, incidents, retention and privacy/consent handling. Every source row, including auto-accepted output, must be reviewed during the real-lab shadow pilot.

[Results JSON](PHASE14_PILOT_RESULTS.json), [error register](PHASE14_PILOT_ERRORS.csv), [document summary](PHASE14_DOCUMENT_SUMMARY.csv), [full row comparison](PHASE14_ROW_COMPARISON.csv), [regression results](PHASE14_REGRESSION_RESULTS.json), [dataset manifest](tests/pilot/manifest.json), [defect evidence](tests/pilot/DEFECT_LOG.md). Frozen before/final machine snapshots are in tests/pilot/runs/baseline and tests/pilot/runs/final. Earlier incomplete/intermediate evidence was retained. Only the isolated pilot containers were stopped; their volumes remain for owner-managed retention.

## Exact git status

Command: `git status --short --branch`

```text
## phase14-pathology-pilot...origin/phase14-pathology-pilot
 M apps/ai-service/core/extractor.py
 M apps/ai-service/parsers/lab_parser.py
 M apps/ai-service/reference_ranges/parser.py
 M apps/api/Controllers/ReportsController.cs
 M apps/api/Services/ReportProcessingService.cs
 M tests/api/Phase10HttpSecurityFactory.cs
?? .gitattributes
?? PHASE14_DOCUMENT_SUMMARY.csv
?? PHASE14_PILOT_ERRORS.csv
?? PHASE14_PILOT_REPORT.md
?? PHASE14_PILOT_RESULTS.json
?? PHASE14_REGRESSION_RESULTS.json
?? PHASE14_ROW_COMPARISON.csv
?? apps/ai-service/tests/test_phase14_regressions.py
?? docs/PATHOLOGY_LAB_PILOT_PROTOCOL.md
?? tests/api/Phase14CorrectionHttpTests.cs
?? tests/api/Phase14PersistenceTests.cs
?? tests/pilot/
```

The complete untracked-file expansion is in tests/pilot/git-status-all.txt. No commit or merge was performed. Work stops at Phase 14.

This pilot is a technical/operational pathology validation and does not constitute broad clinical validation or regulatory approval.
