using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.DocumentReader.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI.DocumentReader.Tests;

public class Phase14CorrectionHttpTests
{
    [Fact]
    public async Task HttpFixtureKeepsStartupSchemaAliveBeforeSeeding()
    {
        await using var factory = new Phase10HttpSecurityFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
        Assert.Equal(0, await db.ProcessingJobs.CountAsync());
    }

    [Fact]
    public async Task CorrectionBindsValidatesAuditsAndPreservesMachineValueOverHttp()
    {
        await using var factory = new Phase10HttpSecurityFactory();
        await factory.SeedAsync();
        var (client, staff) = await factory.LoginAsync("lab-a@test");
        var csrf = await client.GetFromJsonAsync<Phase10HttpSecurityFactory.CsrfResponse>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", csrf!.Token);
        Guid reportId, resultId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
            reportId = (await db.MedicalReports.SingleAsync(x => x.OrganizationId == staff.OrganizationId)).Id;
            resultId = (await db.LabResults.SingleAsync(x => x.MedicalReportId == reportId)).Id;
        }
        var path = $"/api/reports/{reportId}/results/{resultId}";
        var invalid = await client.PatchAsJsonAsync(path, new { value = 10.7m, reason = new string('x', 1001) });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var corrected = await client.PatchAsJsonAsync(path, new { value = 10.7m, valueText = "10.7", reason = "Synthetic review" });
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        var data = await corrected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10.7m, data.GetProperty("valueNumeric").GetDecimal());
        Assert.Equal(10.8m, data.GetProperty("extractedValueNumeric").GetDecimal());
        Assert.Equal("HUMAN_CORRECTED", data.GetProperty("reviewState").GetString());
        var auditResponse = await client.GetAsync(path + "/audit");
        Assert.True(auditResponse.IsSuccessStatusCode, (await auditResponse.Content.ReadAsStringAsync()).Split('\n')[0]);
        var audit = await auditResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, audit.GetArrayLength());
        Assert.Equal("Value", audit[0].GetProperty("fieldName").GetString());
        var verified = await client.PostAsync(path + "/verify", null);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
    }
}
