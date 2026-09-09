# Backup and restore runbook

## SQL Server

From a protected SQL administration host, run a full daily backup with
`BACKUP DATABASE [AiDocumentReaderDb] TO DISK = ... WITH CHECKSUM, INIT`, retain
encrypted backups according to organizational policy, and verify with
`RESTORE VERIFYONLY`. Restore into a separate database name first; validate
tables, row counts, and application health before any cutover.

## Document storage

Snapshot the persistent `report_storage` volume daily. Preserve the stored
server-generated filename and report ownership metadata together. Restore to an
isolated volume, verify that every sampled `StoredFileName` resolves to the
expected PDF, and only then attach it to a test API instance.

## Recovery test

The restore test is a development procedure and must be recorded with backup
timestamp, image version, database schema version, sample report IDs, and
verification result. Production data is never deleted or recreated as part of
this procedure.
