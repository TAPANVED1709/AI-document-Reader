using System.Net;
using System.Net.Http.Json;
using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AI.DocumentReader.Tests;

public sealed class Phase10HttpSecurityFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The hosted worker starts before SeedAsync. Keep the in-memory schema
        // alive across startup scopes instead of racing a vanished database.
        if (_connection.State != System.Data.ConnectionState.Open) _connection.Open();
        builder.UseEnvironment("Development");
        builder.UseSetting("Security:AuthRequestsPerMinute", "100");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<DocumentDbContext>>();
            services.AddDbContext<DocumentDbContext>(options => options.UseSqlite(_connection));
        });
    }

    public async Task SeedAsync()
    {
        if (_connection.State != System.Data.ConnectionState.Open)
        {
            await _connection.OpenAsync();
        }
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
        await db.Database.EnsureCreatedAsync();
        if (await db.ApplicationUsers.AnyAsync()) return;
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<ApplicationUser>();
        var orgA = new Organization { Name = "Synthetic Lab A" }; var orgB = new Organization { Name = "Synthetic Lab B" };
        var patientA = NewUser("patient-a@test", "PATIENT", hasher); var patientB = NewUser("patient-b@test", "PATIENT", hasher);
        var labA = NewUser("lab-a@test", "LAB_STAFF", hasher, orgA.Id); var labB = NewUser("lab-b@test", "LAB_STAFF", hasher, orgB.Id);
        var pathA = NewUser("path-a@test", "PATHOLOGIST", hasher, orgA.Id); var pathB = NewUser("path-b@test", "PATHOLOGIST", hasher, orgB.Id); var pathC = NewUser("path-c@test", "PATHOLOGIST", hasher, orgA.Id); var admin = NewUser("admin@test", "ADMIN", hasher);
        var reportA = NewReport(patientA.Id, orgA.Id, labA.Id); var reportB = NewReport(patientB.Id, orgB.Id, labB.Id); var legacy = NewReport(null, null, null);
        db.AddRange(orgA, orgB, patientA, patientB, labA, labB, pathA, pathB, pathC, admin, reportA, reportB, legacy);
        db.LabResults.AddRange(new LabResult { MedicalReportId = reportA.Id, OriginalTestName = "Hemoglobin", ValueText = "10.8", ValueNumeric = 10.8m, ExtractionConfidence = .9m, PageNumber = 1 }, new LabResult { MedicalReportId = reportB.Id, OriginalTestName = "Hemoglobin", ValueText = "11.2", ValueNumeric = 11.2m, ExtractionConfidence = .9m, PageNumber = 1 });
        db.PatientAccessGrants.AddRange(new PatientAccessGrant { PatientId = patientB.Id, GrantedToUserId = pathA.Id, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) }, new PatientAccessGrant { PatientId = patientB.Id, GrantedToUserId = pathC.Id, ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) }, new PatientAccessGrant { PatientId = patientB.Id, GrantedToUserId = labA.Id, RevokedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    public static ApplicationUser NewUser(string email, string role, Microsoft.AspNetCore.Identity.PasswordHasher<ApplicationUser> hasher, Guid? organizationId = null) { var user = new ApplicationUser { Email = email, Role = role, FirstName = role, OrganizationId = organizationId }; user.PasswordHash = hasher.HashPassword(user, "StrongPassword123!"); return user; }
    private static MedicalReport NewReport(Guid? patient, Guid? organization, Guid? uploader) => new() { OriginalFileName = "synthetic.pdf", StoredFileName = "synthetic.pdf", ContentType = "application/pdf", FileSize = 1, PatientUserId = patient, OrganizationId = organization, UploadedByUserId = uploader };
    public async Task<(HttpClient Client, ApplicationUser User)> LoginAsync(string email)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", csrf!.Token);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "StrongPassword123!" });
        response.EnsureSuccessStatusCode();
        using var scope = Services.CreateScope(); var user = await scope.ServiceProvider.GetRequiredService<DocumentDbContext>().ApplicationUsers.SingleAsync(x => x.Email == email);
        return (client, user);
    }
    public sealed record CsrfResponse(string Token);
}
