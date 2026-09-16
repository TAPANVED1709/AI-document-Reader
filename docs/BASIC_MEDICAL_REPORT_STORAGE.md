# Basic medical report extraction and storage contract

This continues the existing pathology implementation. Numeric classification, applicability, review, corrections, verification, and numeric persistence guards remain separate.

## Field audit

"Before" describes the working tree at the start of the header continuation. Python metadata uses the existing `structuredData` dictionary rather than a second report entity. The API exposes that dictionary unchanged after a database reload. All header paths below are under `MedicalReports.StructuredDataJson`; no new header columns are needed.

| Field | Already extracted before? | Python model/path now | API DTO path | Persisted? / database location | Frontend before → now | Test coverage |
|---|---|---|---|---|---|---|
| PatientName | Partial aliases | patient.name | structuredData.patient.name | Yes / JSON | No → Yes | Alias, PDF, persistence/HTTP, UI |
| PatientId | No | patient.patientId | structuredData.patient.patientId | Yes / JSON | No → Yes | Alias, PDF, persistence/HTTP, UI |
| Age | Raw text | patient.age | structuredData.patient.age | Yes / JSON | No → Yes | Combined/standalone, persistence/HTTP, UI |
| AgeUnit | No | patient.ageUnit | structuredData.patient.ageUnit | Yes / JSON | No → Yes | Years/months, missing unit, UI |
| Sex | Partial aliases | patient.sex | structuredData.patient.sex | Yes / JSON | No → Yes | Combined, ambiguity/conflict, persistence/HTTP, UI |
| DateOfBirth | Raw text | patient.dateOfBirth | structuredData.patient.dateOfBirth | Yes / JSON | No → Yes | ISO, ambiguous dates, persistence/HTTP, UI |
| LaboratoryName | Partial aliases/headings | report.laboratoryName | structuredData.report.laboratoryName | Yes / JSON | No → Yes | Alias, source heading, persistence/HTTP, UI |
| ReportNumber | Partial aliases | report.reportNumber | structuredData.report.reportNumber | Yes / JSON | No → Yes | Alias, persistence/HTTP, UI |
| BookingCode | Partial aliases | report.bookingCode | structuredData.report.bookingCode | Yes / JSON | No → Yes | Alias, persistence/HTTP, UI |
| AccessionNumber | Partial aliases | report.accessionNumber | structuredData.report.accessionNumber | Yes / JSON | No → Yes | Alias, persistence/HTTP, UI |
| ReportGeneratedDate | Explicit reportDate/reportDateIso | report.reportGeneratedDate | structuredData.report.reportGeneratedDate; reportGeneratedDate; summary.reportGeneratedDate | Yes / JSON + existing ReportDate when explicitly available | No header → Yes | Date separation, missing/null, persistence/HTTP, UI |
| CollectionDate | No | report.collectionDate | structuredData.report.collectionDate | Yes / JSON | No → Yes | Alias/date-time, persistence/HTTP, UI |
| CollectionTime | No | report.collectionTime | structuredData.report.collectionTime | Yes / JSON | No → Yes | 12/24 hour, conflict, persistence/HTTP, UI |
| ReferringDoctor | Partial aliases | report.referringDoctor | structuredData.report.referringDoctor | Yes / JSON | No → Yes | Alias, persistence/HTTP, UI |
| SpecimenType | No | report.specimenType | structuredData.report.specimenType | Yes / JSON | No → Yes | Alias, PDF, persistence/HTTP, UI |
| PageCount | Yes | report.pageCount | structuredData.report.pageCount | Yes / JSON | No → Yes | PDF, persistence/HTTP, UI |
| BookingDate | Raw text | report.bookingDate | structuredData.report.bookingDate | Yes / JSON | No → Yes | Date separation, persistence/HTTP, UI |
| TestName | Yes | LabResultItem.normalizedName/originalName | rich results; summary.results.testName | Yes / LabResults.NormalizedTestName/OriginalTestName (+ corrections) | Yes → Yes | Parser, persistence/HTTP, mapper |
| Result | Yes | LabResultItem.value | rich valueNumeric; summary.results.result | Yes / LabResults.ValueNumeric (+ correction) | Yes → Yes | Numeric guards, persistence/HTTP, mapper |
| ResultText | Yes | LabResultItem.valueText | rich valueText; summary.results.resultText | Yes / LabResults.ValueText (+ correction) | Yes → Yes | Qualitative PDF, persistence/HTTP, mapper |
| Unit | Yes | LabResultItem.unit | results.unit | Yes / LabResults.Unit (+ correction) | Yes → Yes | Parser, persistence, mapper |
| ReferenceRange | Yes | LabResultItem.referenceText | rich referenceText; summary.results.referenceRange | Yes / LabResults.ReferenceText (+ correction) | Yes → Yes | Dual printed ranges, persistence, mapper |

## Metadata and date semantics

New records include `patient`, `report`, `rawFields`, and `metadataIssues`. Existing flat metadata keys remain for compatibility. `rawFields` preserves printed date strings and conflicting candidates. No result array is stored inside StructuredDataJson.

Patient IDs printed in the document are strings, not the application's authorization identity `PatientUserId`. Never link an account based on a printed name or ID. Missing values stay null. Age units are only supplied when printed. `22/M` establishes age 22 and sex MALE, but not an age unit; `22Y/M` also establishes YEARS. Age-specific ranges require a known age unit. Sex is never inferred from names or titles. Ambiguous/conflicting sex remains null.

Confident dates normalize to ISO `YYYY-MM-DD`. Supported dates include named English months, ISO year-first dates and unambiguous numeric day/month dates. Ambiguous numeric dates such as `07/03/2004` retain raw text and a metadata issue, with a null normalized date. An explicit time normalizes to `HH:mm`; no timezone is invented. BookingDate, CollectionDate, ReportGeneratedDate, UploadedAt and AnalysedAt have distinct meanings. Missing generated dates never use another date as a substitute. The existing `reportDate` timeline property retains its backward-compatible upload fallback; the new `reportGeneratedDate` and header never use it.

Header aliases require an explicit label delimiter (colon/full-width colon), except recognized laboratory headings. Unsupported layouts remain a limitation. Low-confidence OCR pages cannot establish header demographics.

## Simplified read contract

Authenticated `GET /api/reports/{id}/summary` uses the same report authorization and audit service as report reading. It maps persisted rows, prefers explicit corrections, and does not mutate originals.

```json
{
  "medicalReportId": "<report-guid>",
  "reportGeneratedDate": null,
  "results": [
    { "id": "<result-guid>", "testName": "HBsAg", "result": null, "resultText": "Non-Reactive", "unit": null, "referenceRange": "Non-Reactive" }
  ]
}
```

This is an extraction/integration view, not an assertion that rows are clinically verified. Consumers needing review/applicability/verification state must use the richer report/results API. Qualitative text is preserved with a null numeric result; no synthetic number is assigned. Existing numeric guards remain authoritative. Every result is linked through `LabResults.MedicalReportId`.

## SelfUploadedReportData compatibility

Repository audit found no model, migration, or code path owning `SelfUploadedReportData`. No authorization to alter an externally owned table is established. This change neither creates nor writes that table. `MedicalReportSummaryMapper` is the explicit integration mapper; the richer canonical tables remain authoritative.

The supplied legacy schema (`Id`, `TestName`, `Result decimal(18,5)`, `Unit`, `ReferenceRange`) cannot persist ReportGeneratedDate or ResultText, and has no report/patient foreign keys. It cannot losslessly store qualitative results. Decimal(18,5) permits 13 integer digits and five fractional digits, which differs from this application's existing numeric columns. External writers must validate representability and reject unsupported values rather than clamp, round, or encode qualitative text as numbers.

Any future legacy migration needs the table owner's approval. A lossless destination should include MedicalReportId, an appropriately defined PatientId, ReportGeneratedDate, nullable Result, ResultText, TestName, Unit, ReferenceRange, CalculatedStatus and CreatedAt. No such migration is part of this task.

## Verification files

- Python: `apps/ai-service/tests/test_medical_header.py`, plus all existing classification, OCR and numeric safety tests.
- Persistence and authenticated API: `tests/api/MedicalHeaderTests.cs` exercises actual AiServiceClient JSON deserialization, ReportProcessingService, relational storage, a fresh HTTP reload, the summary endpoint, and unauthorized access rejection.
- Frontend: `apps/web/components/ReportHeader.test.tsx`, plus existing result-status tests.

Real patient PDFs and SQL/API evidence are kept outside the repository. Committed fixtures use synthetic identities.
