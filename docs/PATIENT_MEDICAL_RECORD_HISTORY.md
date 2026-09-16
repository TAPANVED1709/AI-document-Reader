# Patient medical record history (Phase 15B)

The patient workspace reads only canonical `AiDocumentReaderDb.MedicalReports` and `LabResults`. It does not read the medical-software database, re-run OCR to open reports, duplicate result rows, or call a language model to classify results. Phase 15A export and its external five-column schema are unchanged.

## Ownership and endpoints

All `/api/patient/*` endpoints require the PATIENT role. Patient identity comes exclusively from the authenticated NameIdentifier claim. Every report/result query filters `MedicalReport.PatientUserId` by that identity. Supplied patient IDs cannot select another owner. Administrators and staff do not use these endpoints. The shared report/PDF authorization helper also requires exact ownership for patients; uploader, organization and professional grants do not override it. Existing staff authorization remains intact.

- `GET /api/patient/medical-records`: paginated report headers and SQL aggregate counts; `filter=ALL|COMPLETED|NEEDS_REVIEW`, optional `search` across laboratory, original filename and report number.
- `GET /api/patient/medical-records/{reportId}`: persisted metadata, processing state, results and validation issues. No server filename/path, job ID or connection information is returned.
- `GET /api/patient/medical-timeline`: actual reports, oldest dated report first. Undated reports follow, ordered by upload time and explicitly labelled Uploaded in the UI.
- `GET /api/patient/test-history/{normalizedTestName}`: trusted numeric measurements in chronological order, with original name, printed range, unit, laboratory and report linkage.
- `GET /api/patient/latest-results`: latest trusted measurement per stored test name and exact unit.

Lists default to `page=1&pageSize=20`; maximum page size is 100. Invalid pagination is rejected. Responses contain items, total, page and pageSize. History also returns excludedCount. Responses disable caching; audit actions store access events, not result dumps.

## Dates, trust and units

Report-generated dates use the existing explicit metadata mapper, never booking, collection, analysis or upload dates. Null is emitted explicitly. The UI may display `Uploaded <date>` when the generated date is absent. An undated measurement remains in the table, but is not placed on a medical-date chart.

History uses `LabResultTrust.IsTrusted` and excludes conflicting demographic variants from the same report. Numeric overflow, review-required extraction and uncertain/not-applicable demographic applicability are excluded under that existing policy. Human verification follows the existing trust exception; NOT_APPLICABLE and unsafe numerics remain excluded. Inequality/qualified values are not plotted as exact measurements. Persisted normalization groups names; original test names remain available. Corrected numeric/unit/range fields are respected without modifying stored original or verified data.

Every exact unit gets a separate series. No conversion is introduced. Missing units are not plotted. Printed ranges belong to individual measurements; the chart creates no universal normal band. Charts show only the current page of stored measurements, use actual report-date spacing, and provide keyboard-selectable point details and source report links. No predictions, diagnoses or treatment advice are generated.

Numeric classification and demographic applicability remain separate in result cards. Qualitative ValueText is displayed when ValueNumeric is null. Patients can read results but are not shown staff correction/verification controls.

## UI and storage

Navigation provides Dashboard, Upload Report, My Medical Records, Health Timeline and Test Trends. Empty, loading and error states are explicit; navigation reloads a failed screen. Upload reuses Phase 15A multipart/self-upload and patient-owned polling. Report detail reuses the existing header, inline authenticated PDF viewer and result-status component. Desktop/tablet use a split view; mobile stacks the panels.

No patient report-delete endpoint currently exists; this phase adds none. Deletion remains future work requiring coordinated handling of source PDFs, results, validation, audit and secondary export.

The existing LabResults(MedicalReportId) index is reused. An additive/idempotent MedicalReports(PatientUserId) index supports the new ownership predicates. Production operators can preprovision it through the established primary schema process. No external-schema change is made.

## Performance bounds and limitations

There are no per-report authorization or aggregate queries in the new list/history APIs. List counts are calculated in one owner-filtered SQL projection; no PDF bytes or result collections are loaded. Legacy explicit dates and laboratory metadata reside in JSON, so projected headers are sorted/searched in memory before response pagination. History projects only fields required for trust/provenance/display and filters patient/test in SQL, then applies the existing trust function before pagination. These queries are patient-scoped but still materialize that patient's matching headers/measurements; very large archives may require indexed metadata and SQL trust projections later. No such redesign is part of this phase.

The latest-results response preserves separate units. Undated records are explicitly labelled, and dated measurements sort ahead of undated ones; an upload timestamp is not evidence of a newer clinical measurement. Test history expects the stored normalized/corrected test name (the UI labels this field accordingly).

The original PDF viewer depends on the browser's native inline PDF support. A successful PDF HTTP response alone is not proof of visual rendering; live acceptance records that check separately.

## Verification

`PatientRecordsTests` covers owner isolation, forged identity, cross-patient details/PDFs/timeline/history, role restrictions, chronological values, pagination, date provenance, qualitative text, review exclusion and unchanged numeric status under demographic uncertainty. Frontend tests cover loading/empty/error states, opening persisted reports, patient read-only controls, source links, keyboard point selection and unit/date safety. Existing Python and Phase 15A/security regression suites must also pass.
