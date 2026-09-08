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
        }
    }
}
