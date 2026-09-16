using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AI.DocumentReader.Tests;

public class MedicalHeaderTests
{
    [Theory]
    [InlineData("2026-09-16")]
    [InlineData(null)]
    public async Task HeadersSurviveAiClientProcessingDatabaseAndAuthenticatedReload(string? generatedDate)
    {
        await using var factory=new Phase10HttpSecurityFactory(); await factory.SeedAsync();
        var (client,staff)=await factory.LoginAsync("lab-a@test");
        var metadata=new { patient=new { name="Example Patient",patientId="P123",age=22,ageUnit="YEARS",sex="MALE",dateOfBirth="2004-03-07" },
            report=new { laboratoryName="Synthetic Laboratory",reportNumber="R123",bookingCode="B123",accessionNumber="A123",bookingDate="2026-09-14",collectionDate="2026-09-15",collectionTime="08:20",reportGeneratedDate=generatedDate,referringDoctor="Dr Example",specimenType="Serum",pageCount=1 } };
        var response=new { requiresOcr=false,ocrApplied=false,pages=new[]{1},processingMode="NATIVE",documentType="LAB_REPORT",structuredData=metadata,
            results=new[]{new { originalName="HBsAg",normalizedName="HBsAg",value=(decimal?)null,valueText="Non-Reactive",unit=(string?)null,referenceMin=(decimal?)null,referenceMax=(decimal?)null,referenceText="Non-Reactive",page=1,confidence=.95m,referenceType="TEXT_ONLY" }} };
        var file=Path.GetTempFileName(); await File.WriteAllTextAsync(file,"%PDF-1.4 synthetic");
        Guid reportId;
        try
        {
            using var scope=factory.Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
            var report=new MedicalReport { OriginalFileName="synthetic-header.pdf",StoredFileName="synthetic-header.pdf",UploadedByUserId=staff.Id,OrganizationId=staff.OrganizationId };
            reportId=report.Id;
            var job=new ProcessingJob { ReportId=report.Id,Status=ProcessingJobStatus.PROCESSING,AttemptCount=1 };
            db.AddRange(report,job); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            using var transport=new HttpClient(new JsonHandler(JsonSerializer.Serialize(response))){BaseAddress=new Uri("http://synthetic-ai")};
            var ai=new AiServiceClient(transport,NullLogger<AiServiceClient>.Instance);
            var storage=new Mock<ILocalStorageService>(); storage.Setup(s=>s.GetReportAbsolutePath(It.IsAny<string>())).Returns(file);
            await new ReportProcessingService(db,storage.Object,ai,new ReferenceRangeClassifier(),NullLogger<ReportProcessingService>.Instance).ProcessAsync(job,default);
            db.ChangeTracker.Clear();
            var persisted=await db.MedicalReports.Include(r=>r.LabResults).SingleAsync(r=>r.Id==report.Id);
            using var data=JsonDocument.Parse(persisted.StructuredDataJson!);
            Assert.Equal(JsonSerializer.Serialize(metadata),JsonSerializer.Serialize(data.RootElement));
            Assert.False(data.RootElement.TryGetProperty("results",out _));
            Assert.Equal(generatedDate,persisted.ReportDate?.ToString("yyyy-MM-dd"));
            Assert.Null(persisted.PatientUserId); // Printed P123 is not an application authorization identity.
            var row=Assert.Single(persisted.LabResults); Assert.Equal(report.Id,row.MedicalReportId);
            Assert.Null(row.ValueNumeric); Assert.Equal("Non-Reactive",row.ValueText);
        }
        finally { File.Delete(file); }
        // A new HTTP request reloads SQL through a new DbContext, not tracked/transient parser data.
        var loaded=await client.GetFromJsonAsync<JsonElement>($"/api/reports/{reportId}");
        Assert.Equal(JsonSerializer.Serialize(metadata),JsonSerializer.Serialize(loaded.GetProperty("structuredData")));
        Assert.Equal(generatedDate,loaded.GetProperty("reportGeneratedDate").GetString());
        var summary=await client.GetFromJsonAsync<MedicalReportSummary>($"/api/reports/{reportId}/summary");
        Assert.Equal(generatedDate,summary!.ReportGeneratedDate);
        var result=Assert.Single(summary.Results); Assert.Equal("HBsAg",result.TestName); Assert.Null(result.Result); Assert.Equal("Non-Reactive",result.ResultText); Assert.Equal("Non-Reactive",result.ReferenceRange);
        var wire=await client.GetFromJsonAsync<JsonElement>($"/api/reports/{reportId}/summary");
        Assert.Equal(JsonValueKind.Null,wire.GetProperty("results")[0].GetProperty("result").ValueKind);
        Assert.Equal(JsonValueKind.Null,wire.GetProperty("results")[0].GetProperty("unit").ValueKind);
        var (other,_)=await factory.LoginAsync("lab-b@test");
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/reports/{reportId}/summary")).StatusCode);
        using var anonymous=factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync($"/api/reports/{reportId}/summary")).StatusCode);
    }

    [Fact]
    public void SummaryPreservesCorrectedValuesWithoutMutatingExtractedValues()
    {
        var row=new LabResult { OriginalTestName="Hb",NormalizedTestName="Hemoglobin",ValueNumeric=14,ValueText="14",Unit="g/dL",ReferenceText="13-17",CorrectedValueNumeric=14.2m,CorrectedValueText="14.2" };
        var report=new MedicalReport { LabResults=[row],ReportDate=DateTimeOffset.UtcNow,StructuredDataJson="{\"report\":{\"reportGeneratedDate\":null},\"reportDateIso\":\"2026-09-16\"}" };
        var summary=MedicalReportSummaryMapper.Map(report);
        Assert.Null(summary.ReportGeneratedDate); Assert.Equal(14.2m,summary.Results[0].Result); Assert.Equal(14m,row.ValueNumeric);
        Assert.Equal("Hemoglobin",summary.Results[0].TestName); Assert.Equal("13-17",summary.Results[0].ReferenceRange);
    }

    private sealed class JsonHandler(string json): HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Assert.Equal("/analyse",request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json,System.Text.Encoding.UTF8,"application/json")});
        }
    }
}
