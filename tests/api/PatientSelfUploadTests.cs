using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.DocumentReader.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI.DocumentReader.Tests;

public class PatientSelfUploadTests
{
    private static MultipartFormDataContent Pdf(Guid? fakePatient = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("%PDF-1.4\n% Synthetic upload ownership fixture\n%%EOF"u8.ToArray());
        file.Headers.ContentType = new("application/pdf"); form.Add(file, "file", "synthetic.pdf");
        if (fakePatient.HasValue) form.Add(new StringContent(fakePatient.Value.ToString()), "patientUserId");
        return form;
    }

    private static async Task Token(HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        var csrf = await client.GetFromJsonAsync<Phase10HttpSecurityFactory.CsrfResponse>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", csrf!.Token);
    }

    [Fact]
    public async Task SelfUploadRequiresCsrfAndAssignsOnlyAuthenticatedPatientOwnership()
    {
        await using var factory = new Phase10HttpSecurityFactory(); await factory.SeedAsync();
        var (patient, user) = await factory.LoginAsync("patient-a@test");
        patient.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await patient.PostAsync("/api/reports/self-upload", Pdf())).StatusCode);
        patient.DefaultRequestHeaders.Add("X-XSRF-TOKEN", "invalid");
        Assert.Equal(HttpStatusCode.BadRequest, (await patient.PostAsync("/api/reports/self-upload", Pdf())).StatusCode);
        await Token(patient);
        var response = await patient.PostAsync("/api/reports/self-upload", Pdf(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(); var id = payload.GetProperty("reportId").GetGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var report = await scope.ServiceProvider.GetRequiredService<DocumentDbContext>().MedicalReports.SingleAsync(x => x.Id == id);
            Assert.Equal(user.Id, report.PatientUserId); Assert.Equal(user.Id, report.UploadedByUserId); Assert.Null(report.OrganizationId);
        }
        Assert.Equal(HttpStatusCode.OK, (await patient.GetAsync($"/api/reports/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await patient.GetAsync($"/api/reports/{id}/self-processing-status")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.GetAsync("/api/queue/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.GetAsync("/api/review-queue")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await patient.GetAsync($"/api/reports/{id}/file")).StatusCode);
        var (other, _) = await factory.LoginAsync("patient-b@test");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/reports/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/reports/{id}/self-processing-status")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/reports/{id}/medical-software")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/reports/{id}/file")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.PostAsync("/api/reports/upload", Pdf())).StatusCode);
        var (staff, _) = await factory.LoginAsync("lab-a@test"); await Token(staff);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync("/api/reports/self-upload", Pdf())).StatusCode);
        using var anonymous = factory.CreateClient(); await Token(anonymous);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/reports/self-upload", Pdf())).StatusCode);
    }
}
