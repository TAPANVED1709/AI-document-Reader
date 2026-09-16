using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentReader.Api.Infrastructure;

public static class MedicalSoftwareSchema
{
    public const string CreateTableSql = """
        IF OBJECT_ID(N'dbo.SelfUploadedReportData', N'U') IS NULL
        CREATE TABLE [dbo].[SelfUploadedReportData](
            [Id] [bigint] IDENTITY(1,1) NOT NULL,
            [TestName] [nvarchar](max) NULL,
            [Result] [decimal](18, 5) NULL,
            [Unit] [nvarchar](max) NULL,
            [ReferenceRange] [nvarchar](max) NULL,
            PRIMARY KEY CLUSTERED ([Id] ASC)
        ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];
        """;

    // Explicit local-development opt-in only. Production uses separately provisioned DDL.
    public static async Task InitializeDevelopmentAsync(string connectionString, CancellationToken ct = default)
    {
        var settings = new SqlConnectionStringBuilder(connectionString);
        if (settings.InitialCatalog != "MedicalSoftwareIntegrationDb")
            throw new InvalidOperationException("Development initialization requires MedicalSoftwareIntegrationDb.");
        settings.InitialCatalog = "master";
        await using (var master = new SqlConnection(settings.ConnectionString))
        {
            await master.OpenAsync(ct);
            await using var command = master.CreateCommand();
            command.CommandText = "IF DB_ID(N'MedicalSoftwareIntegrationDb') IS NULL CREATE DATABASE [MedicalSoftwareIntegrationDb];";
            await command.ExecuteNonQueryAsync(ct);
        }
        await using var db = new MedicalSoftwareDbContext(new DbContextOptionsBuilder<MedicalSoftwareDbContext>().UseSqlServer(connectionString).Options);
        await db.Database.ExecuteSqlRawAsync(CreateTableSql, ct);
    }
}
