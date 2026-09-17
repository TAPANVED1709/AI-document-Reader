using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI.DocumentReader.Tests;

public class Phase15ClosureTests
{
    private static async Task Csrf(HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        var token = await client.GetFromJsonAsync<Phase10HttpSecurityFactory.CsrfResponse>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token!.Token);
    }

    [Fact]
    public async Task RegistrationValidatesInputAndCannotAssignAdmin()
    {
        await using var factory = new Phase10HttpSecurityFactory(); await factory.SeedAsync();
        using var client = factory.CreateClient(); await Csrf(client);
        foreach (var input in new[] {
            new { email="invalid", password="StrongPassword123!", firstName="Test", lastName="Patient", role="ADMIN" },
            new { email="new@test.example", password="short", firstName="Test", lastName="Patient", role="ADMIN" },
            new { email="new@test.example", password="StrongPassword123!", firstName=" ", lastName="Patient", role="ADMIN" } })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", input)).StatusCode);
        var request = new { email="new@test.example", password="StrongPassword123!", firstName="Test", lastName="Patient", role="ADMIN" };
        var response = await client.PostAsJsonAsync("/api/auth/register", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("password", text.ToLowerInvariant());
        Assert.Equal("PATIENT", JsonDocument.Parse(text).RootElement.GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", request)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", request)).StatusCode);
    }

    [Fact]
    public async Task FailedAndPendingRecordsStayOutOfHistoryWithoutDeletingEvidence()
    {
        await using var factory = new Phase10HttpSecurityFactory(); await factory.SeedAsync();
        var (client, patient) = await factory.LoginAsync("patient-a@test"); await Csrf(client);
        var ids = new List<Guid>();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
            foreach (var status in new[] { ReportStatus.Failed, ReportStatus.Processing, ReportStatus.Uploaded })
            {
                var r = new MedicalReport { PatientUserId=patient.Id, Status=status, OriginalFileName="invalid.pdf", StoredFileName="missing.pdf" };
                r.LabResults.Add(new LabResult { OriginalTestName="Excluded", NormalizedTestName="Excluded", ValueNumeric=1, ValueText="1", CalculatedStatus=ResultStatus.NORMAL });
                db.Add(r); ids.Add(r.Id);
            }
            await db.SaveChangesAsync();
        }
        foreach (var path in new[] { "/api/patient/medical-records", "/api/patient/medical-timeline", "/api/patient/latest-results", "/api/patient/test-history/Excluded", "/api/timeline", "/api/timeline/latest-results", "/api/timeline/tests/Excluded", "/api/trends/Excluded" })
        {
            var response=await client.GetAsync(path);response.EnsureSuccessStatusCode();var text=await response.Content.ReadAsStringAsync();
            Assert.All(ids, id => Assert.DoesNotContain(id.ToString(), text, StringComparison.OrdinalIgnoreCase));
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/patient/medical-records/{ids[0]}")).StatusCode);
        // Owned processing details remain available outside clinical history.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/reports/{ids[0]}")).StatusCode);
        var (other, _) = await factory.LoginAsync("patient-b@test");
        foreach(var suffix in new[] { "", "/file", "/self-processing-status" })
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/reports/{ids[0]}{suffix}")).StatusCode);
        using var check = factory.Services.CreateScope();
        Assert.Equal(3, await check.ServiceProvider.GetRequiredService<DocumentDbContext>().MedicalReports.CountAsync(r=>ids.Contains(r.Id)));
    }

    [Fact]
    public async Task MissingSourceIsExplicitAndLegacyFallbackIsLabeled()
    {
        await using var factory = new Phase10HttpSecurityFactory(); await factory.SeedAsync();
        var (client, patient) = await factory.LoginAsync("patient-a@test");
        Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
            var report=new MedicalReport { PatientUserId=patient.Id, Status=ReportStatus.Completed, StoredFileName="missing.pdf", OriginalFileName="missing.pdf", ReportDate=null, ReportDateSource="DOCUMENT", StructuredDataJson="{\"report\":{\"reportGeneratedDate\":null}}" };
            report.LabResults.Add(new LabResult { OriginalTestName="Example",NormalizedTestName="Example",ValueNumeric=1,ValueText="1",CalculatedStatus=ResultStatus.NORMAL });
            db.Add(report); await db.SaveChangesAsync(); id=report.Id;
        }
        foreach(var path in new[] { $"/api/reports/{id}",$"/api/patient/medical-records/{id}" })
        {
            var value=await client.GetFromJsonAsync<JsonElement>(path);
            Assert.False(value.GetProperty("sourceFileAvailable").GetBoolean());
            Assert.DoesNotContain("storedFileName",value.GetRawText()); Assert.DoesNotContain("storage/",value.GetRawText());
        }
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/reports/{id}/file")).StatusCode);
        var trend=await client.GetFromJsonAsync<JsonElement>("/api/trends/Example");
        Assert.Equal("UPLOAD_DATE",trend.GetProperty("points")[0].GetProperty("dateSource").GetString());
        var history=await client.GetFromJsonAsync<JsonElement>("/api/patient/test-history/Example");
        Assert.Equal(JsonValueKind.Null,history.GetProperty("items")[0].GetProperty("reportDate").ValueKind);
    }

    [Theory]
    [InlineData(20 * 1024 * 1024 + 1)]
    [InlineData(22 * 1024 * 1024)]
    public async Task OversizedUploadReturns413WithoutCreatingReport(int length)
    {
        await using var factory = new Phase10HttpSecurityFactory(); await factory.SeedAsync();
        var (client, _) = await factory.LoginAsync("patient-a@test"); await Csrf(client);
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<DocumentDbContext>();var before=await db.MedicalReports.CountAsync();
        using var form=new MultipartFormDataContent();var data=new byte[length];"%PDF-"u8.CopyTo(data);
        var content=new ByteArrayContent(data);content.Headers.ContentType=new("application/pdf");form.Add(content,"file","oversized.pdf");
        var response=await client.PostAsync("/api/reports/self-upload",form);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,response.StatusCode);
        Assert.Contains("20 MB upload limit",await response.Content.ReadAsStringAsync());
        Assert.Equal(before,await db.MedicalReports.CountAsync());
    }
}
