using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI.DocumentReader.Tests;

public class PatientRecordsTests
{
    private static async Task<(Guid[] Reports, Guid Foreign, Guid Undated)> HistorySeed(Phase10HttpSecurityFactory factory)
    {
        await factory.SeedAsync();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
        var a = await db.ApplicationUsers.SingleAsync(x => x.Email == "patient-a@test");
        var b = await db.ApplicationUsers.SingleAsync(x => x.Email == "patient-b@test");
        var reports = new List<MedicalReport>();
        for (var i = 0; i < 7; i++)
        {
            var report = new MedicalReport
            {
                PatientUserId = i == 6 ? b.Id : a.Id, UploadedByUserId = a.Id, OriginalFileName = $"history-{i}.pdf", StoredFileName = "synthetic.pdf", ContentType = "application/pdf",
                Status = i == 3 ? ReportStatus.RequiresReview : ReportStatus.Completed, DocumentType = "LAB_REPORT", UploadedAt = DateTimeOffset.Parse("2026-09-16T12:00:00Z"),
                // Legacy misleading timeline dates must not become generated dates.
                ReportDate = DateTimeOffset.Parse("2026-04-01T00:00:00Z"), ReportDateSource = "COLLECTION_DATE",
                StructuredDataJson = JsonSerializer.Serialize(new { patient = new { name = "Synthetic", sex = (string?)null }, report = new {
                    laboratoryName = "Synthetic Lab", reportNumber = $"H-{i}", reportGeneratedDate = i == 5 ? null : $"2026-{new[] { 5, 7, 9, 9, 9, 9, 9 }[i]:00}-01",
                    bookingDate = "2026-03-01", collectionDate = "2026-04-01" } })
            };
            report.LabResults.Add(new LabResult {
                OriginalTestName = "Serum Creatinine", NormalizedTestName = "Creatinine", ValueNumeric = i == 3 ? 6392437665405192000m : i == 4 ? 97m : .9m + i * .1m,
                ValueText = i == 3 ? "6392437665405192000" : "stored", Unit = i == 4 ? "µmol/L" : "mg/dL", ReferenceText = i == 4 ? "62-115" : "0.7-1.3",
                CalculatedStatus = ResultStatus.NORMAL, ReviewRequired = i == 3, ExtractionConfidence = .95m
            });
            reports.Add(report); db.Add(report);
        }
        reports[0].LabResults.Add(new LabResult { OriginalTestName = "HBsAg", ValueText = "Non-Reactive", ReviewRequired = true });
        reports[6].LabResults.Add(new LabResult { OriginalTestName = "ForeignOnly", NormalizedTestName = "ForeignOnly", ValueText = "1" });
        reports[0].LabResults.Add(new LabResult { OriginalTestName = "Hb", NormalizedTestName = "Hemoglobin", ValueNumeric = 14, ValueText = "14", Unit = "g/dL", CalculatedStatus = ResultStatus.NORMAL,
            DemographicQualifier = "MALE", ApplicabilityStatus = "REVIEW_REQUIRED", ApplicabilityReason = "PATIENT_SEX_MISSING", ReviewRequired = true });
        await db.SaveChangesAsync();
        return (reports.Take(6).Select(r => r.Id).ToArray(), reports[6].Id, reports[5].Id);
    }
    private static async Task<JsonElement> Get(HttpClient client, string path)
    {
        var response = await client.GetAsync(path); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task OwnerScopedListsDetailsPdfsAndHistoryIgnoreForgedIdentity()
    {
        await using var factory = new Phase10HttpSecurityFactory(); var seed = await HistorySeed(factory);
        var (client, user) = await factory.LoginAsync("patient-a@test");
        var list = await Get(client, $"/api/patient/medical-records?patientId={seed.Foreign}&search=history-");
        Assert.Equal(6, list.GetProperty("total").GetInt32());
        Assert.All(list.GetProperty("items").EnumerateArray(), r => Assert.Contains(r.GetProperty("reportId").GetGuid(), seed.Reports));
        foreach (var path in new[] { $"/api/patient/medical-records/{seed.Foreign}", $"/api/reports/{seed.Foreign}", $"/api/reports/{seed.Foreign}/file", $"/api/reports/{seed.Foreign}/medical-software" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/reports/{seed.Reports[0]}/file")).StatusCode);
        foreach (var path in new[] { "/api/patient/medical-timeline", "/api/patient/test-history/Creatinine", "/api/patient/latest-results" })
        {
            var body = await Get(client, path + "?patientId=" + seed.Foreign);
            Assert.DoesNotContain(body.GetProperty("items").EnumerateArray(), r => r.GetProperty("reportId").GetGuid() == seed.Foreign);
        }
        var legacy = await Get(client, "/api/timeline");
        Assert.DoesNotContain(legacy.EnumerateArray(), r => r.GetProperty("reportId").GetGuid() == seed.Foreign);
        var detail = await Get(client, $"/api/patient/medical-records/{seed.Reports[0]}");
        var legacyDetail = await Get(client, $"/api/reports/{seed.Reports[0]}"); Assert.False(legacyDetail.TryGetProperty("storedFileName", out _));
        var names = await Get(client, "/api/trends/tests"); Assert.DoesNotContain(names.EnumerateArray(), value => value.GetString() == "ForeignOnly");
        var serialized = detail.GetRawText();
        foreach (var forbidden in new[] { "storedFileName", "passwordHash", "connectionString", "jobId", "workerId" }) Assert.DoesNotContain(forbidden, serialized);
        Assert.Equal("Synthetic", detail.GetProperty("patient").GetProperty("name").GetString());
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
        Assert.True(await db.SecurityAuditEvents.AnyAsync(x => x.Action == "MEDICAL_RECORD_LIST"));
    }

    [Fact]
    public async Task DatesTrustUnitsQualitativeAndNumericStatusRemainIndependent()
    {
        await using var factory = new Phase10HttpSecurityFactory(); var seed = await HistorySeed(factory); var (client, _) = await factory.LoginAsync("patient-a@test");
        var history = await Get(client, "/api/patient/test-history/Creatinine"); var points = history.GetProperty("items").EnumerateArray().ToArray();
        var datedMg = points.Where(p => p.GetProperty("unit").GetString() == "mg/dL" && p.GetProperty("reportDate").ValueKind != JsonValueKind.Null).ToArray();
        Assert.Equal(new[] { .9m, 1m, 1.1m }, datedMg.Select(p => p.GetProperty("value").GetDecimal()));
        Assert.Equal(seed.Reports.Take(3), datedMg.Select(p => p.GetProperty("reportId").GetGuid()));
        Assert.DoesNotContain(points, p => p.GetProperty("reportId").GetGuid() == seed.Reports[3]);
        Assert.Equal(1, history.GetProperty("excludedCount").GetInt32());
        Assert.Contains(points, p => p.GetProperty("unit").GetString() == "µmol/L" && p.GetProperty("value").GetDecimal() == 97m);
        Assert.Equal(JsonValueKind.Null, points.Single(p => p.GetProperty("reportId").GetGuid() == seed.Undated).GetProperty("reportDate").ValueKind);
        var all = await Get(client, "/api/patient/medical-records?search=history-&pageSize=2");
        Assert.Equal(6, all.GetProperty("total").GetInt32()); Assert.Equal(2, all.GetProperty("items").GetArrayLength());
        var timeline = await Get(client, "/api/patient/medical-timeline");
        Assert.Equal("2026-05-01", timeline.GetProperty("items")[0].GetProperty("reportGeneratedDate").GetString());
        var filtered = await Get(client, "/api/patient/medical-records?filter=NEEDS_REVIEW&search=history-");
        Assert.Contains(filtered.GetProperty("items").EnumerateArray(), p => p.GetProperty("reportId").GetGuid() == seed.Reports[3]);
        var detail = await Get(client, $"/api/patient/medical-records/{seed.Reports[0]}"); var rows = detail.GetProperty("results").EnumerateArray().ToArray();
        var qualitative = rows.Single(r => r.GetProperty("originalTestName").GetString() == "HBsAg");
        Assert.Equal("Non-Reactive", qualitative.GetProperty("valueText").GetString());
        Assert.False(qualitative.TryGetProperty("valueNumeric", out var number) && number.ValueKind != JsonValueKind.Null);
        var hemoglobin = rows.Single(r => r.GetProperty("originalTestName").GetString() == "Hb");
        Assert.Equal("NORMAL", hemoglobin.GetProperty("calculatedStatus").GetString());
        Assert.Equal("REVIEW_REQUIRED", hemoglobin.GetProperty("applicabilityStatus").GetString());
        var hb = await Get(client, "/api/patient/test-history/Hemoglobin"); Assert.DoesNotContain(hb.GetProperty("items").EnumerateArray(), p => p.GetProperty("reportId").GetGuid() == seed.Reports[0]); Assert.Equal(1, hb.GetProperty("excludedCount").GetInt32());
        var missingDate = await Get(client, $"/api/patient/medical-records/{seed.Undated}"); Assert.Equal(JsonValueKind.Null, missingDate.GetProperty("reportGeneratedDate").ValueKind);
    }

    [Fact]
    public async Task HumanVerificationDoesNotAdmitOverflowNotApplicableOrQualifiedMeasurements()
    {
        await using var factory = new Phase10HttpSecurityFactory(); var seed = await HistorySeed(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
            var hb = await db.LabResults.SingleAsync(x => x.MedicalReportId == seed.Reports[0] && x.NormalizedTestName == "Hemoglobin"); hb.IsVerified = true;
            var overflow = await db.LabResults.SingleAsync(x => x.MedicalReportId == seed.Reports[3]); overflow.IsVerified = true;
            var notApplicable = await db.LabResults.SingleAsync(x => x.MedicalReportId == seed.Reports[1]); notApplicable.ApplicabilityStatus = "NOT_APPLICABLE"; notApplicable.IsVerified = true;
            var qualified = await db.LabResults.SingleAsync(x => x.MedicalReportId == seed.Reports[2]); qualified.ValueOperator = "<"; qualified.IsVerified = true;
            await db.SaveChangesAsync();
        }
        var (client, _) = await factory.LoginAsync("patient-a@test");
        var hbHistory = await Get(client, "/api/patient/test-history/Hemoglobin");
        Assert.Contains(hbHistory.GetProperty("items").EnumerateArray(), p => p.GetProperty("reportId").GetGuid() == seed.Reports[0]);
        var history = await Get(client, "/api/patient/test-history/Creatinine");
        Assert.Equal(3, history.GetProperty("excludedCount").GetInt32());
        Assert.All(history.GetProperty("items").EnumerateArray(), p => Assert.DoesNotContain(p.GetProperty("reportId").GetGuid(), seed.Reports.Skip(1).Take(3)));
    }

    [Fact]
    public async Task AnonymousStaffAdminAndInvalidPagingAreRejectedAndEmptyPatientIsSafe()
    {
        await using var factory = new Phase10HttpSecurityFactory(); await factory.SeedAsync(); using var anonymous = factory.CreateClient();
        var paths = new[] { "/api/patient/medical-records", "/api/patient/medical-records/" + Guid.NewGuid(), "/api/patient/medical-timeline", "/api/patient/test-history/Creatinine", "/api/patient/latest-results" };
        foreach (var path in paths) Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        foreach (var email in new[] { "lab-a@test", "admin@test" })
        {
            var (client, _) = await factory.LoginAsync(email);
            foreach (var path in paths) Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        }
        var (patient, _) = await factory.LoginAsync("patient-a@test");
        foreach (var query in new[] { "page=0", "pageSize=101", "pageSize=0", "filter=DISEASE" })
            Assert.Equal(HttpStatusCode.BadRequest, (await patient.GetAsync("/api/patient/medical-records?" + query)).StatusCode);
        var empty = await Get(patient, "/api/patient/test-history/AbsentSyntheticTest"); Assert.Equal(0, empty.GetProperty("total").GetInt32());
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
        db.Add(Phase10HttpSecurityFactory.NewUser("empty@test", "PATIENT", new Microsoft.AspNetCore.Identity.PasswordHasher<ApplicationUser>())); await db.SaveChangesAsync();
        var (emptyPatient, _) = await factory.LoginAsync("empty@test"); var records = await Get(emptyPatient, "/api/patient/medical-records");
        Assert.Equal(0, records.GetProperty("items").GetArrayLength()); Assert.Equal(0, records.GetProperty("total").GetInt32());
    }
}
