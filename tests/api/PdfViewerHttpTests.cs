using System.Net;
using System.Net.Http.Headers;
using AI.DocumentReader.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI.DocumentReader.Tests;

public class PdfViewerHttpTests : IClassFixture<Phase10HttpSecurityFactory>
{
    private readonly Phase10HttpSecurityFactory _factory;
    public PdfViewerHttpTests(Phase10HttpSecurityFactory factory)
    { _factory = factory; factory.SeedAsync().GetAwaiter().GetResult(); }

    private Guid ReportId()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
        var patient = db.ApplicationUsers.Single(x => x.Email == "patient-a@test").Id;
        return db.MedicalReports.Single(x => x.PatientUserId == patient).Id;
    }

    [Theory]
    [InlineData("patient-a@test")]
    [InlineData("lab-a@test")]
    public async Task Authorized_pdf_is_inline_and_frames_only_exact_configured_origins(string email)
    {
        var (client, _) = await _factory.LoginAsync(email);
        using var response = await client.GetAsync($"/api/reports/{ReportId()}/file");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Null(response.Content.Headers.ContentDisposition?.FileName);
        Assert.Contains("bytes", response.Headers.AcceptRanges);
        Assert.StartsWith("%PDF-", await response.Content.ReadAsStringAsync());
        Assert.False(response.Headers.Contains("X-Frame-Options"));
        Assert.Equal("frame-ancestors http://localhost:3001 https://viewer.synthetic.test:8443",
            response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Authorized_range_request_returns_the_requested_bytes_inline()
    {
        var (client, _) = await _factory.LoginAsync("patient-a@test");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/reports/{ReportId()}/file");
        request.Headers.Range = new RangeHeaderValue(0, 4);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("%PDF-", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, response.Content.Headers.ContentRange?.From);
        Assert.Equal(4, response.Content.Headers.ContentRange?.To);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.False(response.Headers.Contains("X-Frame-Options"));
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("patient-b@test", HttpStatusCode.NotFound)]
    [InlineData("lab-b@test", HttpStatusCode.NotFound)]
    public async Task Unauthorized_or_cross_owner_pdf_is_denied_and_cannot_be_framed(string? email, HttpStatusCode status)
    {
        var client = email is null ? _factory.CreateClient() : (await _factory.LoginAsync(email)).Client;
        using var response = await client.GetAsync($"/api/reports/{ReportId()}/file");
        Assert.Equal(status, response.StatusCode);
        AssertDeny(response);
        Assert.DoesNotContain("%PDF-", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Normal_api_and_nonexistent_file_routes_keep_frame_protection()
    {
        var (client, _) = await _factory.LoginAsync("patient-a@test");
        foreach (var path in new[] { "/health", $"/api/reports/{ReportId()}", $"/api/reports/{Guid.NewGuid()}/file", $"/api/reports/{ReportId()}/file/extra" })
        {
            using var response = await client.GetAsync(path);
            AssertDeny(response);
        }
    }

    private static void AssertDeny(HttpResponseMessage response)
    {
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }
}
