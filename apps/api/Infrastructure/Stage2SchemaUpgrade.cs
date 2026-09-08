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
        };
        var correctionColumns = new Dictionary<string, string>
        {
            ["CorrectedTestName"] = "TEXT NULL", ["CorrectedValueNumeric"] = "REAL NULL",
            ["CorrectedValueText"] = "TEXT NULL", ["CorrectedUnit"] = "TEXT NULL",
            ["CorrectedReferenceMin"] = "REAL NULL", ["CorrectedReferenceMax"] = "REAL NULL",
            ["CorrectedReferenceText"] = "TEXT NULL", ["CorrectionReason"] = "TEXT NULL",
            ["CorrectedAt"] = "TEXT NULL", ["CorrectedBy"] = "TEXT NULL"
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
                    await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS ResultCorrectionAudits (Id TEXT PRIMARY KEY NOT NULL, LabResultId TEXT NOT NULL, FieldName TEXT NOT NULL, PreviousValue TEXT NULL, NewValue TEXT NULL, Reason TEXT NULL, ChangedAt TEXT NOT NULL, ChangedBy TEXT NULL, FOREIGN KEY (LabResultId) REFERENCES LabResults(Id) ON DELETE CASCADE)");
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
            await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID('ResultCorrectionAudits', 'U') IS NULL CREATE TABLE ResultCorrectionAudits (Id uniqueidentifier NOT NULL PRIMARY KEY, LabResultId uniqueidentifier NOT NULL, FieldName nvarchar(100) NOT NULL, PreviousValue nvarchar(500) NULL, NewValue nvarchar(500) NULL, Reason nvarchar(1000) NULL, ChangedAt datetimeoffset NOT NULL, ChangedBy nvarchar(255) NULL, CONSTRAINT FK_ResultCorrectionAudits_LabResults FOREIGN KEY (LabResultId) REFERENCES LabResults(Id) ON DELETE CASCADE)");
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
