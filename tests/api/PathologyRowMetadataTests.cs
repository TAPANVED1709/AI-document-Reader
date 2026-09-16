using System.Text.Json;
using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AI.DocumentReader.Tests;

public class PathologyRowMetadataTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataAndEveryRowPersistSeparatelyWithSafePrintedRangeClassification(bool printedReportDate)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new DocumentDbContext(new DbContextOptionsBuilder<DocumentDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var patient = Guid.NewGuid(); var org = Guid.NewGuid(); var uploader = Guid.NewGuid();
        var report = new MedicalReport { OriginalFileName = "synthetic.pdf", StoredFileName = "synthetic.pdf", PatientUserId = patient, OrganizationId = org, UploadedByUserId = uploader };
        var job = new ProcessingJob { ReportId = report.Id, Status = ProcessingJobStatus.PROCESSING, AttemptCount = 1 };
        db.AddRange(report, job); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var rows = new List<AiLabResultDto> {
            Row("Total Protein",6.9m,"g/dL",6,8.3m,"6-8.3 g/dL / 60-83 g/L"),
            Row("GGT",10,"U/L",9,48,"9-48 U/L / 0.2-0.8 µkat/L"),
            Row("Sodium",167,"mEq/L",135,145,"135-145 mEq/L / 135-145 mmol/L"),
            Row("Ionized Calcium",1.9m,"mmol/L",1.1m,1.3m,"1.1-1.3 mmol/L / 1.1-1.3 mmol/L"),
            Row("Serum Creatinine (Male)",.9m,"mg/dL",.7m,1.3m,"0.7-1.3 mg/dL") with { ReviewRequired=true, DemographicQualifier="MALE", ApplicabilityStatus="REVIEW_REQUIRED", ApplicabilityReason="PATIENT_SEX_MISSING" },
            Row("Serum Creatinine (Female)",.9m,"mg/dL",.6m,1.1m,"0.6-1.1 mg/dL") with { ReviewRequired=true, DemographicQualifier="FEMALE", ApplicabilityStatus="REVIEW_REQUIRED", ApplicabilityReason="PATIENT_SEX_MISSING" },
            Row("Low confidence",.9m,"mg/dL",.7m,1.3m,"0.7-1.3 mg/dL") with { Confidence=.4m },
            Row("Hemoglobin (Female)",16,"g/dL",12,15,"12-15 g/dL") with { NormalizedName="Hemoglobin", DemographicQualifier="FEMALE", ApplicabilityStatus="REVIEW_REQUIRED", ApplicabilityReason="PATIENT_SEX_MISSING" },
            Row("Alpha-1 Antitrypsin",70,"mg/dL",90,200,"90-200 mg/dL / 0-0 mg/dL") with { Confidence=.7m, ReviewRequired=true, FieldConfidences=new() { ["name"] = .7m, ["value"] = .95m, ["ref"] = .95m } },
        };
        var metadata = JsonSerializer.Deserialize<Dictionary<string,object>>("""
            {"laboratoryName":"SYNTHETIC HEALTHCARE LABORATORY","patientName":"Example Patient","age":null,"sex":null,"dateOfBirth":null,"bookingCode":"SYN-12345","bookingDate":"08 Sep 2026","sections":["Biochemistry"],"pageCount":1}
            """)!;
        if (printedReportDate) metadata["reportDateIso"] = "2026-09-08";
        var storage = new Mock<ILocalStorageService>(); storage.Setup(s => s.GetReportAbsolutePath(It.IsAny<string>())).Returns("synthetic.pdf");
        var ai = new Mock<IAiServiceClient>(); ai.Setup(s => s.AnalyseDocumentAsync(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiAnalysisResponseDto(false,false,[1],rows,DocumentType:"LAB_REPORT",StructuredData:metadata));
        await new ReportProcessingService(db,storage.Object,ai.Object,new ReferenceRangeClassifier(),NullLogger<ReportProcessingService>.Instance).ProcessAsync(job,default);
        db.ChangeTracker.Clear(); var saved = await db.MedicalReports.SingleAsync();
        Assert.Equal("LAB_REPORT",saved.DocumentType); Assert.Equal(patient,saved.PatientUserId); Assert.Equal(org,saved.OrganizationId); Assert.Equal(uploader,saved.UploadedByUserId);
        using var data = JsonDocument.Parse(saved.StructuredDataJson!);
        Assert.Equal("Example Patient",data.RootElement.GetProperty("patientName").GetString());
        Assert.Equal(JsonValueKind.Null,data.RootElement.GetProperty("age").ValueKind);
        Assert.Equal(JsonValueKind.Null,data.RootElement.GetProperty("dateOfBirth").ValueKind);
        Assert.False(data.RootElement.TryGetProperty("results",out _));
        if (printedReportDate) { Assert.Equal(new DateTimeOffset(2026,9,8,0,0,0,TimeSpan.Zero),saved.ReportDate); Assert.Equal("DOCUMENT",saved.ReportDateSource); }
        else Assert.Null(saved.ReportDate);
        var persisted = await db.LabResults.ToListAsync(); Assert.Equal(9,persisted.Count); Assert.All(persisted,r => Assert.Equal(saved.Id,r.MedicalReportId));
        foreach (var name in new[]{"Total Protein","GGT"}) Assert.Equal(ResultStatus.NORMAL,persisted.Single(r=>r.OriginalTestName==name).CalculatedStatus);
        foreach (var name in new[]{"Sodium","Ionized Calcium"}) Assert.Equal(ResultStatus.HIGH,persisted.Single(r=>r.OriginalTestName==name).CalculatedStatus);
        Assert.All(persisted.Where(r=>r.OriginalTestName.Contains("Creatinine")), r => {
            Assert.Equal(ResultStatus.NORMAL,r.CalculatedStatus); Assert.True(r.ReviewRequired);
            Assert.Equal("REVIEW_REQUIRED",r.ApplicabilityStatus); Assert.Equal("PATIENT_SEX_MISSING",r.ApplicabilityReason);
            Assert.Contains(r.DemographicQualifier,new[]{"MALE","FEMALE"});
        });
        Assert.Equal(ResultStatus.UNKNOWN,persisted.Single(r=>r.OriginalTestName=="Low confidence").CalculatedStatus);
        Assert.Equal(ResultStatus.HIGH,persisted.Single(r=>r.OriginalTestName=="Hemoglobin (Female)").CalculatedStatus);
        Assert.Equal(ResultStatus.LOW,persisted.Single(r=>r.OriginalTestName=="Alpha-1 Antitrypsin").CalculatedStatus);
        Assert.Equal(rows[0].ReferenceText,persisted.Single(r=>r.OriginalTestName=="Total Protein").ReferenceText);
        Assert.Equal(ProcessingJobStatus.REVIEW_REQUIRED,(await db.ProcessingJobs.SingleAsync()).Status);
    }

    private static AiLabResultDto Row(string name, decimal value,string unit,decimal min,decimal max,string reference)
        => new(name,name,value,value.ToString(System.Globalization.CultureInfo.InvariantCulture),unit,min,max,reference,1,.95m,
            OriginalUnit:unit,NormalizedUnit:unit,ReferenceType:"BETWEEN",Section:"Biochemistry");
}
