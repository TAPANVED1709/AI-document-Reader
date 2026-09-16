using Microsoft.EntityFrameworkCore;

namespace AI.DocumentReader.Api.Infrastructure;

public static class MedicalReportExportSchema
{
    public static Task ApplyAsync(DocumentDbContext db) => db.Database.ExecuteSqlRawAsync(db.Database.IsSqlServer() ? """
        IF OBJECT_ID(N'dbo.MedicalReportExports', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.MedicalReportExports (
                Id uniqueidentifier NOT NULL PRIMARY KEY,
                MedicalReportId uniqueidentifier NOT NULL,
                Destination nvarchar(80) NOT NULL,
                Status nvarchar(30) NOT NULL,
                AttemptCount int NOT NULL,
                CreatedAt datetimeoffset NOT NULL,
                LastAttemptAt datetimeoffset NULL,
                NextAttemptAt datetimeoffset NULL,
                CompletedAt datetimeoffset NULL,
                LastErrorCode nvarchar(80) NULL,
                LastErrorSafeMessage nvarchar(500) NULL,
                ExternalRowIdsJson nvarchar(max) NULL,
                [RowCount] int NOT NULL,
                SuppressedNumericCount int NOT NULL,
                CONSTRAINT FK_MedicalReportExports_MedicalReports FOREIGN KEY (MedicalReportId) REFERENCES MedicalReports(Id) ON DELETE CASCADE
            );
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MedicalReportExports') AND name=N'IX_MedicalReportExports_MedicalReportId_Destination')
            CREATE UNIQUE INDEX IX_MedicalReportExports_MedicalReportId_Destination ON dbo.MedicalReportExports(MedicalReportId, Destination);
        """ : """
        CREATE TABLE IF NOT EXISTS MedicalReportExports (
            Id TEXT NOT NULL PRIMARY KEY, MedicalReportId TEXT NOT NULL, Destination TEXT NOT NULL, Status TEXT NOT NULL,
            AttemptCount INTEGER NOT NULL, CreatedAt TEXT NOT NULL, LastAttemptAt TEXT NULL, NextAttemptAt TEXT NULL,
            CompletedAt TEXT NULL, LastErrorCode TEXT NULL, LastErrorSafeMessage TEXT NULL, ExternalRowIdsJson TEXT NULL,
            RowCount INTEGER NOT NULL, SuppressedNumericCount INTEGER NOT NULL,
            FOREIGN KEY (MedicalReportId) REFERENCES MedicalReports(Id) ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_MedicalReportExports_MedicalReportId_Destination ON MedicalReportExports(MedicalReportId, Destination);
        """);
}
