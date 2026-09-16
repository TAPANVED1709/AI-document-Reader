using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI.DocumentReader.Tests;

public class ApplicabilityTests
{
    [Theory]
    [InlineData("REVIEW_REQUIRED", true, false, false)]
    [InlineData("REVIEW_REQUIRED", true, true, true)]
    [InlineData("NOT_APPLICABLE", true, true, false)]
    [InlineData("APPLICABLE", false, false, true)]
    [InlineData("NOT_REQUIRED", true, false, false)]
    public void TrustIsIndependentOfNumericClassification(string applicability, bool review, bool verified, bool trusted)
    {
        var row = new LabResult { ValueNumeric=.7m, CalculatedStatus=ResultStatus.NORMAL, ApplicabilityStatus=applicability, ReviewRequired=review, IsVerified=verified };
        Assert.Equal(trusted,LabResultTrust.IsTrusted(row));
        Assert.Equal(verified ? "HUMAN_VERIFIED" : review ? "REVIEW_REQUIRED" : "AUTO_ACCEPTED",LabResultTrust.ReviewState(row));
        row.ValueNumeric=6392437665405192000m;
        Assert.False(LabResultTrust.IsTrusted(row));
    }

    [Theory]
    [InlineData(.95, .95, .95, true)]
    [InlineData(.4, .95, .95, false)]
    [InlineData(.95, .4, .95, false)]
    [InlineData(.95, .95, .4, false)]
    public void ClassificationUsesValueRangeAndAssociationConfidence(double value, double reference, double association, bool classify)
    {
        var row = new LabResult { ValueNumeric=.7m, ReferenceMin=.7m, ReferenceMax=1.3m, ExtractionConfidence=.7m, ReviewRequired=true, ApplicabilityStatus="REVIEW_REQUIRED" };
        Assert.Equal(classify,LabClassificationSafety.CanClassify(row,new Dictionary<string,decimal>{["name"]=.7m,["value"]=(decimal)value,["ref"]=(decimal)reference,["association"]=(decimal)association}));
    }

    [Fact]
    public async Task AdditiveSqliteUpgradeIsIdempotentAndPreservesCorrectedVerifiedRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new DocumentDbContext(new DbContextOptionsBuilder<DocumentDbContext>().UseSqlite(connection).Options);
        // A pre-upgrade schema with existing data, deliberately lacking the three new columns.
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE MedicalReports (Id TEXT PRIMARY KEY); CREATE TABLE LabResults (Id TEXT PRIMARY KEY, MedicalReportId TEXT, ValueNumeric TEXT, CorrectedValueNumeric TEXT, IsVerified INTEGER); INSERT INTO MedicalReports VALUES ('report'); INSERT INTO LabResults VALUES ('result','report','0.7','0.8',1)");
        await Stage2SchemaUpgrade.ApplyAsync(db); await Stage2SchemaUpgrade.ApplyAsync(db);
        using var command = connection.CreateCommand();
        command.CommandText="SELECT ValueNumeric,CorrectedValueNumeric,IsVerified,DemographicQualifier,ApplicabilityStatus,ApplicabilityReason FROM LabResults WHERE Id='result'";
        using var reader=await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        Assert.Equal("0.7",reader.GetString(0)); Assert.Equal("0.8",reader.GetString(1)); Assert.Equal(1,reader.GetInt32(2));
        Assert.True(reader.IsDBNull(3)); Assert.Equal("NOT_REQUIRED",reader.GetString(4)); Assert.True(reader.IsDBNull(5));
        var model=db.Model.FindEntityType(typeof(LabResult))!;
        Assert.Equal(40,model.FindProperty(nameof(LabResult.DemographicQualifier))!.GetMaxLength());
        Assert.Equal(40,model.FindProperty(nameof(LabResult.ApplicabilityStatus))!.GetMaxLength());
        Assert.Equal(255,model.FindProperty(nameof(LabResult.ApplicabilityReason))!.GetMaxLength());
    }

    [Fact]
    public async Task HttpDtoAndTrustedTimelineKeepDemographicStatesIndependent()
    {
        await using var factory=new Phase10HttpSecurityFactory(); await factory.SeedAsync();
        var (client,staff)=await factory.LoginAsync("lab-a@test");
        Guid reportId,maleId,femaleId;
        using(var scope=factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
            var report=await db.MedicalReports.SingleAsync(r=>r.OrganizationId==staff.OrganizationId); reportId=report.Id;
            var male=await db.LabResults.SingleAsync(r=>r.MedicalReportId==reportId); maleId=male.Id;
            male.OriginalTestName="Hemoglobin (Male)"; male.NormalizedTestName="Hemoglobin"; male.ValueNumeric=14; male.ValueText="14";
            male.CalculatedStatus=ResultStatus.NORMAL; male.DemographicQualifier="MALE"; male.ApplicabilityStatus="REVIEW_REQUIRED"; male.ApplicabilityReason="PATIENT_SEX_MISSING"; male.ReviewRequired=true;
            var female=new LabResult { MedicalReportId=reportId, OriginalTestName="Hemoglobin (Female)", NormalizedTestName="Hemoglobin", ValueNumeric=16, ValueText="16", Unit="g/dL", CalculatedStatus=ResultStatus.HIGH, DemographicQualifier="FEMALE", ApplicabilityStatus="REVIEW_REQUIRED", ApplicabilityReason="PATIENT_SEX_MISSING", ReviewRequired=true };
            femaleId=female.Id; db.Add(female); await db.SaveChangesAsync();
        }
        var reportDto=await client.GetFromJsonAsync<JsonElement>($"/api/reports/{reportId}");
        var maleDto=reportDto.GetProperty("results").EnumerateArray().Single(r=>r.GetProperty("id").GetGuid()==maleId);
        Assert.Equal("NORMAL",maleDto.GetProperty("calculatedStatus").GetString());
        Assert.Equal("MALE",maleDto.GetProperty("demographicQualifier").GetString());
        Assert.Equal("REVIEW_REQUIRED",maleDto.GetProperty("applicabilityStatus").GetString());
        Assert.Equal("PATIENT_SEX_MISSING",maleDto.GetProperty("applicabilityReason").GetString());
        Assert.Equal("REVIEW_REQUIRED",maleDto.GetProperty("reviewState").GetString());
        var latest=await client.GetFromJsonAsync<JsonElement>("/api/timeline/latest-results");
        Assert.Empty(latest.EnumerateArray()); // Includes neither these rows nor the other organization's fixture.
        var csrf=await client.GetFromJsonAsync<Phase10HttpSecurityFactory.CsrfResponse>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-XSRF-TOKEN",csrf!.Token);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsync($"/api/reports/{reportId}/results/{maleId}/verify",null)).StatusCode);
        latest=await client.GetFromJsonAsync<JsonElement>("/api/timeline/latest-results");
        Assert.Equal(maleId,Assert.Single(latest.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsync($"/api/reports/{reportId}/results/{femaleId}/verify",null)).StatusCode);
        latest=await client.GetFromJsonAsync<JsonElement>("/api/timeline/latest-results");
        Assert.Empty(latest.EnumerateArray()); // Both variants verified still cannot be simultaneous trusted values.
        var trend=await client.PostAsJsonAsync("/api/trends/compare",new { reportIds=new[]{reportId}, testName="Hemoglobin" });
        Assert.Equal(HttpStatusCode.OK,trend.StatusCode);
        var points=(await trend.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("points");
        Assert.Equal(2,points.GetArrayLength());
        Assert.All(points.EnumerateArray(),p=> { Assert.False(p.GetProperty("included").GetBoolean()); Assert.Equal("DEMOGRAPHIC_VARIANTS_CONFLICT",p.GetProperty("exclusionReason").GetString()); });
    }
}
