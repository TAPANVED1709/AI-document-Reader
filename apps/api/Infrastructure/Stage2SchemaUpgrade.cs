using Microsoft.EntityFrameworkCore;

namespace AI.DocumentReader.Api.Infrastructure;

// Additive upgrade for the existing EnsureCreated-based Stage 1 database.
// DDL identifiers/types below are fixed source constants, never user input.
#pragma warning disable EF1002
public static class Stage2SchemaUpgrade
{
    public static async Task ApplyAsync(DocumentDbContext db)
    {
        if (!db.Database.IsRelational()) return;
        var columns = new Dictionary<string, string>
        {
            ["OcrRequired"] = "INTEGER NOT NULL DEFAULT 0",
            ["OcrApplied"] = "INTEGER NOT NULL DEFAULT 0",
            ["ProcessingMode"] = "TEXT NOT NULL DEFAULT 'UNKNOWN'",
            ["PageSourcesJson"] = "TEXT NULL"
            , ["ReportDate"] = "TEXT NULL", ["ReportDateSource"] = "TEXT NOT NULL DEFAULT 'UPLOAD_DATE'"
            , ["DocumentType"] = "TEXT NOT NULL DEFAULT 'UNKNOWN'", ["DocumentTypeConfidence"] = "REAL NOT NULL DEFAULT 0"
            , ["DocumentTypeSignalsJson"] = "TEXT NULL", ["StructuredDataJson"] = "TEXT NULL"
            , ["PatientUserId"] = "TEXT NULL", ["OrganizationId"] = "TEXT NULL", ["UploadedByUserId"] = "TEXT NULL"
        };
        var correctionColumns = new Dictionary<string, string>
        {
            ["CorrectedTestName"] = "TEXT NULL", ["CorrectedValueNumeric"] = "REAL NULL",
            ["CorrectedValueText"] = "TEXT NULL", ["CorrectedUnit"] = "TEXT NULL",
            ["CorrectedReferenceMin"] = "REAL NULL", ["CorrectedReferenceMax"] = "REAL NULL",
            ["CorrectedReferenceText"] = "TEXT NULL", ["CorrectionReason"] = "TEXT NULL",
            ["CorrectedAt"] = "TEXT NULL", ["CorrectedBy"] = "TEXT NULL",
            ["OriginalUnit"] = "TEXT NULL", ["NormalizedUnit"] = "TEXT NULL", ["ValueOperator"] = "TEXT NULL",
            ["ReferenceType"] = "TEXT NOT NULL DEFAULT 'UNKNOWN'", ["ReferenceOperator"] = "TEXT NULL", ["ReportedFlag"] = "TEXT NULL",
            ["SectionName"] = "TEXT NULL", ["MethodText"] = "TEXT NULL", ["ReviewRequired"] = "INTEGER NOT NULL DEFAULT 0",
            ["AmbiguityReason"] = "TEXT NULL", ["FlagDiscrepancy"] = "TEXT NULL"
        };
        if (db.Database.IsSqlite())
        {
            await db.Database.OpenConnectionAsync();
            try
            {
                var existing = new HashSet<string>();
                using (var command = db.Database.GetDbConnection().CreateCommand())
                {
                    command.CommandText = "PRAGMA table_info(MedicalReports)";
                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync()) existing.Add(reader.GetString(1));
                }
                foreach (var (name, definition) in columns)
                    if (!existing.Contains(name))
                        await db.Database.ExecuteSqlRawAsync($"ALTER TABLE MedicalReports ADD COLUMN {name} {definition}");
                if (await TableExistsAsync(db, "LabResults"))
                {
                    existing = await ExistingColumnsAsync(db, "LabResults");
                    foreach (var (name, definition) in correctionColumns)
                        if (!existing.Contains(name))
                            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE LabResults ADD COLUMN {name} {definition}");
                    await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS ExplanationRecords (Id TEXT PRIMARY KEY NOT NULL, MedicalReportId TEXT NOT NULL, LabResultId TEXT NULL, Mode TEXT NOT NULL, Provider TEXT NOT NULL, Model TEXT NOT NULL, PromptVersion TEXT NOT NULL, GeneratedText TEXT NOT NULL, GeneratedJson TEXT NOT NULL, CreatedAt TEXT NOT NULL, FOREIGN KEY (MedicalReportId) REFERENCES MedicalReports(Id) ON DELETE CASCADE)");
                    await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS ValidationIssues (Id TEXT PRIMARY KEY NOT NULL, LabResultId TEXT NOT NULL, Code TEXT NOT NULL, Severity TEXT NOT NULL, FieldName TEXT NULL, Message TEXT NOT NULL, RequiresReview INTEGER NOT NULL DEFAULT 1, IsResolved INTEGER NOT NULL DEFAULT 0, ResolutionType TEXT NULL, CreatedAt TEXT NOT NULL, ResolvedAt TEXT NULL, FOREIGN KEY (LabResultId) REFERENCES LabResults(Id) ON DELETE CASCADE)");
                    await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS ResultCorrectionAudits (Id TEXT PRIMARY KEY NOT NULL, LabResultId TEXT NOT NULL, FieldName TEXT NOT NULL, PreviousValue TEXT NULL, NewValue TEXT NULL, Reason TEXT NULL, ChangedAt TEXT NOT NULL, ChangedBy TEXT NULL, FOREIGN KEY (LabResultId) REFERENCES LabResults(Id) ON DELETE CASCADE)");
                    await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS PatientAccessGrants (Id TEXT PRIMARY KEY NOT NULL, PatientId TEXT NOT NULL, GrantedToUserId TEXT NULL, GrantedToOrganizationId TEXT NULL, Scope TEXT NOT NULL, CreatedAt TEXT NOT NULL, ExpiresAt TEXT NULL, RevokedAt TEXT NULL, GrantedByUserId TEXT NOT NULL)");
                }
            }
            finally { await db.Database.CloseConnectionAsync(); }
        }
        else if (db.Database.IsSqlServer())
        {
            foreach (var (name, definition) in columns)
            {
                var sqlType = definition.Replace("INTEGER", "BIT").Replace("TEXT", "NVARCHAR(MAX)");
                await db.Database.ExecuteSqlRawAsync($"IF COL_LENGTH('MedicalReports', '{name}') IS NULL ALTER TABLE MedicalReports ADD {name} {sqlType}");
            }
            foreach (var (name, definition) in correctionColumns)
            {
                var sqlType = definition.Replace("REAL", "DECIMAL(18,4)").Replace("TEXT", "NVARCHAR(MAX)");
                await db.Database.ExecuteSqlRawAsync($"IF COL_LENGTH('LabResults', '{name}') IS NULL ALTER TABLE LabResults ADD {name} {sqlType}");
            }
            await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID('ExplanationRecords', 'U') IS NULL CREATE TABLE ExplanationRecords (Id uniqueidentifier NOT NULL PRIMARY KEY, MedicalReportId uniqueidentifier NOT NULL, LabResultId uniqueidentifier NULL, Mode nvarchar(40) NOT NULL, Provider nvarchar(50) NOT NULL, Model nvarchar(100) NOT NULL, PromptVersion nvarchar(50) NOT NULL, GeneratedText nvarchar(max) NOT NULL, GeneratedJson nvarchar(max) NOT NULL, CreatedAt datetimeoffset NOT NULL, CONSTRAINT FK_ExplanationRecords_MedicalReports FOREIGN KEY (MedicalReportId) REFERENCES MedicalReports(Id) ON DELETE CASCADE)");
            await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID('ValidationIssues', 'U') IS NULL CREATE TABLE ValidationIssues (Id uniqueidentifier NOT NULL PRIMARY KEY, LabResultId uniqueidentifier NOT NULL, Code nvarchar(100) NOT NULL, Severity nvarchar(20) NOT NULL, FieldName nvarchar(100) NULL, Message nvarchar(1000) NOT NULL, RequiresReview bit NOT NULL DEFAULT 1, IsResolved bit NOT NULL DEFAULT 0, ResolutionType nvarchar(50) NULL, CreatedAt datetimeoffset NOT NULL, ResolvedAt datetimeoffset NULL, CONSTRAINT FK_ValidationIssues_LabResults FOREIGN KEY (LabResultId) REFERENCES LabResults(Id) ON DELETE CASCADE)");
            await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID('ResultCorrectionAudits', 'U') IS NULL CREATE TABLE ResultCorrectionAudits (Id uniqueidentifier NOT NULL PRIMARY KEY, LabResultId uniqueidentifier NOT NULL, FieldName nvarchar(100) NOT NULL, PreviousValue nvarchar(500) NULL, NewValue nvarchar(500) NULL, Reason nvarchar(1000) NULL, ChangedAt datetimeoffset NOT NULL, ChangedBy nvarchar(255) NULL, CONSTRAINT FK_ResultCorrectionAudits_LabResults FOREIGN KEY (LabResultId) REFERENCES LabResults(Id) ON DELETE CASCADE)");
            await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID('PatientAccessGrants', 'U') IS NULL CREATE TABLE PatientAccessGrants (Id uniqueidentifier NOT NULL PRIMARY KEY, PatientId uniqueidentifier NOT NULL, GrantedToUserId uniqueidentifier NULL, GrantedToOrganizationId uniqueidentifier NULL, Scope nvarchar(50) NOT NULL, CreatedAt datetimeoffset NOT NULL, ExpiresAt datetimeoffset NULL, RevokedAt datetimeoffset NULL, GrantedByUserId uniqueidentifier NOT NULL)");
        }
    }

    private static async Task<HashSet<string>> ExistingColumnsAsync(DocumentDbContext db, string table)
    {
        var existing = new HashSet<string>();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) existing.Add(reader.GetString(1));
        return existing;
    }

    private static async Task<bool> TableExistsAsync(DocumentDbContext db, string table)
    {
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name";
        var parameter = command.CreateParameter(); parameter.ParameterName = "$name"; parameter.Value = table; command.Parameters.Add(parameter);
        return await command.ExecuteScalarAsync() is not null;
    }
}
