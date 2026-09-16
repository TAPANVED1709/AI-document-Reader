using Microsoft.EntityFrameworkCore;

namespace AI.DocumentReader.Api.Infrastructure;

public static class PatientHistorySchema
{
    // Owner-scoped list/detail/history reads now use this key. The LabResults foreign-key index already exists.
    public static Task ApplyAsync(DocumentDbContext db) => db.Database.ExecuteSqlRawAsync(db.Database.IsSqlServer() ? """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MedicalReports') AND name=N'IX_MedicalReports_PatientUserId')
            CREATE INDEX IX_MedicalReports_PatientUserId ON dbo.MedicalReports(PatientUserId);
        """ : "CREATE INDEX IF NOT EXISTS IX_MedicalReports_PatientUserId ON MedicalReports(PatientUserId);");
}
