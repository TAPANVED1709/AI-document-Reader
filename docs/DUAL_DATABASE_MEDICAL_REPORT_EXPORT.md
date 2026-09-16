# Dual database medical report export (Phase 15A)

## Responsibilities and transaction boundary

`AiDocumentReaderDb` remains authoritative for PDFs, patient ownership, organizations, complete LabResults, qualitative text, demographics, classification/applicability, review, corrections, verification, jobs and audit records. None of those models is replaced by the external schema.

`MedicalSoftwareIntegrationDb.dbo.SelfUploadedReportData` is an extraction/output table for the other medical software. It is **not** a patient record or a clinically verified dataset: the supplied schema has no patient/report key, review status, qualifier, method, qualitative text or report date. Consumers requiring those meanings must use the authenticated canonical API. Export does not approve or verify a result.

Processing commits canonical results, terminal ProcessingJob state and one `MedicalReportExport` intent in the **same primary SaveChanges transaction**. A separate hosted export worker reads that committed intent. It inserts the external rows in a transaction belonging solely to the secondary context. There is no distributed transaction. A secondary failure changes only the export ledger, never the canonical report/job.

The primary ledger schema must initialize successfully before the API accepts uploads. This check uses only the primary database. A missing/unavailable secondary database does not block startup or primary persistence.

The integration is opt-in. Successfully processed LAB_REPORT documents with results receive PENDING intents when enabled. Disabled integration, non-laboratory documents, unavailable OCR and empty results receive NOT_REQUIRED. Existing historical reports are not silently backfilled. Reprocessing an existing report does not replace its export intent or corrected/verified results.

## Configuration

Server-side settings:

```text
ConnectionStrings__DefaultConnection=Server=sqlserver,1433;Database=AiDocumentReaderDb;User Id=<primary-user>;Password=<secret>;...
ConnectionStrings__MedicalSoftware=Server=sqlserver,1433;Database=MedicalSoftwareIntegrationDb;User Id=<export-user>;Password=<secret>;...
MedicalSoftwareExport__Enabled=true
MedicalSoftwareExport__MaxAttempts=3
MedicalSoftwareExport__RetryBaseSeconds=10
```

Docker Compose maps `MEDICAL_SOFTWARE_EXPORT_ENABLED=true` from the ignored local `.env` to the API's Enabled setting. The connection string goes only to the API container. No `NEXT_PUBLIC_*` variable or browser code contains it. Production should use a secret store, encrypted SQL transport with validated certificates and a least-privileged database user. The `.env.example` password is a placeholder, not a credential.

## Development initialization and production provisioning

For a native Development launch, explicitly set `MedicalSoftwareExport__InitializeDevelopmentDatabase=true`. The initializer accepts only the development database name `MedicalSoftwareIntegrationDb`; it creates that database and the table if absent. It never drops/rebuilds an existing table. This initialization is not run in Production, even if the flag is present. An initialization failure does not prevent primary processing.

The local Docker API uses Production middleware. Provision its development database using the additive/idempotent `infra/sql/medical-software-development.sql`, with the administrator of the local SQL container, before enabling export. For example, copy the script into the container and invoke sqlcmd with `SQLCMDPASSWORD` supplied from the container's existing local environment. Do not paste credentials into command history or commit them. Running the script repeatedly preserves existing data.

For production, the database owner provisions the destination database independently, runs the following table DDL if absent, and grants the API account connection, table INSERT and SELECT on the Id column for EF's generated-key OUTPUT. SQL Server requires SELECT on columns returned by OUTPUT ([Microsoft documentation](https://learn.microsoft.com/en-us/sql/t-sql/queries/output-clause-transact-sql#permissions)). Runtime does not need SA, CREATE DATABASE or ALTER TABLE on the destination. Provision the additive `MedicalReportExports` table/index in the primary database through the existing schema deployment process (`MedicalReportExportSchema` supplies its DDL). Existing canonical tables are not rebuilt.

```sql
CREATE TABLE [dbo].[SelfUploadedReportData](
    [Id] [bigint] IDENTITY(1,1) NOT NULL,
    [TestName] [nvarchar](max) NULL,
    [Result] [decimal](18, 5) NULL,
    [Unit] [nvarchar](max) NULL,
    [ReferenceRange] [nvarchar](max) NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];
```

The external table has exactly these five columns. `SelfUploadedReportData` is mapped exclusively by `MedicalSoftwareDbContext`, never by `DocumentDbContext`. No external migration or ResultText/date/ownership column is added.

## Mapping and response

| Canonical source | External destination |
|---|---|
| OriginalTestName | TestName |
| ValueNumeric, if exactly representable | Result |
| Unit | Unit |
| ReferenceText | ReferenceRange |

This is a one-time snapshot of **original extracted fields**. Normalized/corrected test names do not replace the source name. Subsequent corrections remain authoritative in the canonical system; they do not silently update or re-export external rows.

Authenticated `GET /api/reports/{id}/medical-software` projects the original canonical fields with the same numeric policy. It does not require the external database to be available. It is a read view, not proof of successful delivery; read `GET /api/reports/{id}/medical-software/status` for delivery state. Both routes enforce the existing report ownership/grant/organization authorization and audit access. No global external-table read endpoint exists.

```json
{
  "reportGeneratedDate": "2026-09-16",
  "results": [
    { "testName": "Hemoglobin", "result": 13.4, "unit": "g/dL", "referenceRange": "12-15" },
    { "testName": "Creatinine", "result": 1.1, "unit": "mg/dL", "referenceRange": "0.7-1.3" }
  ]
}
```

JSON numbers need not retain SQL's five display zeros. SQL stores decimal(18,5).

`reportGeneratedDate` is obtained exclusively from explicitly extracted canonical report-date metadata using the existing date mapper. A missing date is emitted as JSON null. UploadedAt, AnalysedAt, BookingDate, CollectionDate and the legacy timeline upload-date fallback are never substitutes. The date exists in PRIMARY/API only; it cannot be stored in the five-column secondary table.

## Qualitative and numeric safety

Qualitative rows are inserted with Result=NULL (strategy A). HBsAg Non-Reactive retains ValueNumeric=null and ValueText=Non-Reactive in the primary database. The external row contains the source name/unit/range but **cannot convey Non-Reactive**, because there is no ResultText column. NULL is not zero, negative, normal or an omitted test. The external contract cannot distinguish qualitative, missing and suppressed numeric results; consult canonical data.

The secondary decimal permits values from -9999999999999.99999 through 9999999999999.99999 with at most five significant fractional places. Both mapping and the writer check range and exact representability. Values are never rounded/clamped. Unrepresentable secondary numbers become NULL; `SuppressedNumericCount` records this in the primary export ledger. Primary-safe values exceeding the smaller external integer range remain unchanged in primary storage. Existing primary numeric guards continue preserving truly malformed OCR as review evidence. A bad candidate cannot abort valid neighbors in the export.

## Idempotency and bounded retries

The primary ledger has a unique constraint on `(MedicalReportId, Destination)`; destination is SELF_UPLOADED_REPORT_DATA. States are NOT_REQUIRED, PENDING, IN_PROGRESS, COMPLETED and FAILED. A conditional SQL update atomically claims PENDING/due FAILED records and increments AttemptCount. Competing processes cannot claim the same attempt. COMPLETED records are never replayed. Uploading the same PDF as a **new MedicalReport** is a different export, not a retry.

Each external batch uses one explicit transaction. Generated external IDs are recorded in `ExternalRowIdsJson` before committing that transaction, to support reconciliation. There is no EF automatic retry strategy on external writes.

Connection/open failures before row writes, and failures with an acknowledged rollback, are safe to retry. They record SECONDARY_DATABASE_UNAVAILABLE and retry with exponential backoff (10s, 20s by default), bounded at three attempts. Successful retry needs no PDF re-upload. Exhaustion records FAILED with no next attempt.

**Commit ambiguity is not automatically retried.** The immutable external schema has no idempotency key. If COMMIT is sent but acknowledgement is lost, or a process dies after external commit and before primary completion, automatically inserting again could duplicate rows. Such attempts remain fenced IN_PROGRESS and, after five minutes, become FAILED/EXPORT_RECONCILIATION_REQUIRED, or are immediately marked that way when detected. NextAttemptAt stays null. Cancellation and unknown failures are conservative. The canonical job remains COMPLETED/REVIEW_REQUIRED.

## Failure recovery

1. Restore destination availability/schema/permissions. Retryable failures automatically retry within the bound.
2. For exhausted **known-rollback** failures, an authorized operator can inspect the ledger and reset AttemptCount/NextAttemptAt for that FAILED record through an approved primary-database operation. Never create a second ledger row or re-upload the PDF merely to retry delivery.
3. For reconciliation-required attempts, first ensure the old worker/transaction is no longer active. Use the recorded external IDs plus canonical expected rows and database transaction evidence to determine whether the entire secondary batch committed. If all rows committed, mark the existing ledger COMPLETED; if rollback/non-delivery is conclusively established, schedule the existing record for retry. An empty visible query while an old transaction is still active is **not** proof of rollback. If uncertain, leave it stopped. Do not reset it blindly.
4. Coordinate restores of both databases. Restoring only one database can invalidate export history; reconcile rather than clearing the ledger.

No exception text, connection string, SQL parameters or PHI is stored in error messages. Worker logs contain only export IDs, counts and fixed operational messages. Secondary EF logging is suppressed. External row IDs are operational linkage retained only in primary storage.

## Patient self-upload and security

The existing staff `/api/reports/upload` route remains restricted to LAB_STAFF/PATHOLOGIST. A separate `/api/reports/self-upload` route permits PATIENT only and sets PatientUserId/UploadedByUserId from authenticated claims. Multipart patient IDs cannot select an owner. The existing UI selects this route and the ownership-checked `/api/reports/{id}/self-processing-status` polling route for a signed-in patient; staff queue/status restrictions remain unchanged; both routes use the same file validation, CSRF middleware, queue and processing pipeline. Patient reads continue using the canonical report's authorization; the external table grants no access. This phase adds no patient dashboard and no direct browser-to-SQL access.

## Verification

`MedicalSoftwareExportTests` exercises separate relational databases, happy path, null qualitative export, overflow/range/scale, primary retention during outage, bounded retry, completed replay, concurrent claim, rollback and ambiguous-commit fencing, ledger uniqueness, report-date provenance and authenticated ownership. `PatientSelfUploadTests` checks CSRF, authenticated ownership, ignored forged patient identity and role separation. Frontend API tests cover patient routing, credentials, multipart boundaries and session restoration. Live Docker acceptance additionally checks actual SQL Server decimal/schema behavior and the real PDF.
