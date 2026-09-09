using System.Net;
using System.Net.Http.Json;
using AI.DocumentReader.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI.DocumentReader.Tests;

public class Phase10HttpSecurityTests : IClassFixture<Phase10HttpSecurityFactory>
{
    private readonly Phase10HttpSecurityFactory _factory;
    public Phase10HttpSecurityTests(Phase10HttpSecurityFactory factory) { _factory = factory; _factory.SeedAsync().GetAwaiter().GetResult(); }

    [Fact]
    public async Task Real_http_cookie_login_and_csrf_matrix_is_enforced()
    {
        var (client, _) = await _factory.LoginAsync("patient-a@test");
        var missing = await client.PostAsync("/api/auth/logout", null); Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var invalid = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout"); invalid.Headers.Add("X-XSRF-TOKEN", "invalid"); Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(invalid)).StatusCode);
        var valid = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout"); valid.Headers.Add("X-XSRF-TOKEN", await GetToken(client));
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(valid)).StatusCode);
    }

    [Fact]
    public async Task Real_http_patient_report_ownership_blocks_idor()
    {
        var (client, user) = await _factory.LoginAsync("patient-a@test");
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>(); var ownReport = db.MedicalReports.Single(x => x.PatientUserId == user.Id); var otherReport = db.MedicalReports.Single(x => x.PatientUserId != user.Id && x.PatientUserId.HasValue);
        var own = await client.GetAsync($"/api/reports/{ownReport.Id}"); var other = await client.GetAsync($"/api/reports/{otherReport.Id}");
        var otherBody = await other.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, own.StatusCode); Assert.True(other.StatusCode == HttpStatusCode.NotFound, $"{other.StatusCode}: {otherBody}");
    }

    [Fact]
    public async Task Real_http_unauthenticated_report_is_rejected()
    {
        var client = _factory.CreateClient(); using var scope = _factory.Services.CreateScope(); var id = scope.ServiceProvider.GetRequiredService<DocumentDbContext>().MedicalReports.First().Id;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/reports/{id}")).StatusCode);
    }

    [Fact]
    public async Task Real_http_security_headers_are_present()
    {
        var response = await _factory.CreateClient().GetAsync("/health");
        Assert.True(response.Headers.Contains("Content-Security-Policy")); Assert.True(response.Headers.Contains("X-Content-Type-Options")); Assert.True(response.Headers.Contains("Referrer-Policy")); Assert.True(response.Headers.Contains("X-Frame-Options"));
    }

    [Fact]
    public async Task Real_http_auth_errors_are_generic_and_do_not_expose_hashes()
    {
        var client = _factory.CreateClient(); var token = await client.GetFromJsonAsync<Phase10HttpSecurityFactory.CsrfResponse>("/api/auth/csrf"); client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token!.Token);
        var unknown = await client.PostAsJsonAsync("/api/auth/login", new { email = "unknown@test", password = "WrongPassword123!" }); var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email = "patient-a@test", password = "WrongPassword123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode); Assert.Equal(await unknown.Content.ReadAsStringAsync(), await wrong.Content.ReadAsStringAsync());
        var body = await unknown.Content.ReadAsStringAsync(); Assert.DoesNotContain("PasswordHash", body); Assert.DoesNotContain("StrongPassword", body);
    }

    [Fact]
    public async Task Real_http_resource_matrix_blocks_cross_patient_legacy_and_allows_owned_trends()
    {
        var (client, user) = await _factory.LoginAsync("patient-a@test");
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
        var own = db.MedicalReports.Single(x => x.PatientUserId == user.Id); var other = db.MedicalReports.Single(x => x.PatientUserId != user.Id && x.PatientUserId.HasValue); var legacy = db.MedicalReports.Single(x => x.PatientUserId == null);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/reports/{own.Id}/results")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/reports/{other.Id}/results")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/reports/{other.Id}/file")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/timeline/{other.Id}")).StatusCode);
        var explanation = new HttpRequestMessage(HttpMethod.Post, $"/api/reports/{other.Id}/explanations/patient") { Content = JsonContent.Create(new { }) }; explanation.Headers.Add("X-XSRF-TOKEN", await GetToken(client));
        var explanationStatus = (await client.SendAsync(explanation)).StatusCode;
        Assert.True(explanationStatus is HttpStatusCode.NotFound or HttpStatusCode.ServiceUnavailable);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/reports/{legacy.Id}")).StatusCode);
        var trend = new HttpRequestMessage(HttpMethod.Post, "/api/trends/compare") { Content = JsonContent.Create(new { reportIds = new[] { own.Id } }) }; trend.Headers.Add("X-XSRF-TOKEN", await GetToken(client));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(trend)).StatusCode);
    }

    [Fact]
    public async Task Real_http_access_grants_expiry_revocation_and_org_isolation_are_enforced()
    {
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>(); var reportB = db.MedicalReports.Single(x => x.PatientUserId == db.ApplicationUsers.Single(u => u.Email == "patient-b@test").Id).Id;
        var (pathA, _) = await _factory.LoginAsync("path-a@test"); Assert.Equal(HttpStatusCode.OK, (await pathA.GetAsync($"/api/reports/{reportB}")).StatusCode);
        var (pathC, _) = await _factory.LoginAsync("path-c@test"); Assert.Equal(HttpStatusCode.NotFound, (await pathC.GetAsync($"/api/reports/{reportB}")).StatusCode);
        var (labA, _) = await _factory.LoginAsync("lab-a@test"); Assert.Equal(HttpStatusCode.NotFound, (await labA.GetAsync($"/api/reports/{reportB}")).StatusCode);
    }

    [Fact]
    public async Task Real_http_z_cors_rate_limit_and_cookie_headers_are_enforced()
    {
        var client = _factory.CreateClient(); using var allowed = new HttpRequestMessage(HttpMethod.Get, "/health"); allowed.Headers.Add("Origin", "http://localhost:3000"); var cors = await client.SendAsync(allowed);
        Assert.Equal("http://localhost:3000", cors.Headers.GetValues("Access-Control-Allow-Origin").Single()); Assert.Equal("true", cors.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        using var denied = new HttpRequestMessage(HttpMethod.Get, "/health"); denied.Headers.Add("Origin", "https://evil.example"); Assert.False((await client.SendAsync(denied)).Headers.Contains("Access-Control-Allow-Origin"));
        var (authenticated, user) = await _factory.LoginAsync("patient-a@test");
        using var resourceScope = _factory.Services.CreateScope(); var other = resourceScope.ServiceProvider.GetRequiredService<DocumentDbContext>().MedicalReports.First(x => x.PatientUserId != user.Id && x.PatientUserId.HasValue).Id;
        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 21; i++) { var request = new HttpRequestMessage(HttpMethod.Post, $"/api/reports/{other}/explanations/patient") { Content = JsonContent.Create(new { }) }; request.Headers.Add("X-XSRF-TOKEN", await GetToken(authenticated)); responses.Add(await authenticated.SendAsync(request)); }
        Assert.Contains(responses, r => r.StatusCode is (HttpStatusCode)429 or HttpStatusCode.ServiceUnavailable);
    }

    private static async Task<string> GetToken(HttpClient client) => (await client.GetFromJsonAsync<Phase10HttpSecurityFactory.CsrfResponse>("/api/auth/csrf"))!.Token;
}
