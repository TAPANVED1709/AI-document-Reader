using System.Security.Claims;
using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AI.DocumentReader.Tests;

public class Phase10SecurityTests
{
    private static (DocumentDbContext Db, ApplicationUser Patient, ApplicationUser Other, MedicalReport Report) Fixture()
    {
        var options = new DbContextOptionsBuilder<DocumentDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new DocumentDbContext(options);
        var patient = new ApplicationUser { Email = "a@example.test", Role = "PATIENT" };
        var other = new ApplicationUser { Email = "b@example.test", Role = "PATIENT" };
        var report = new MedicalReport { OriginalFileName = "synthetic.pdf", StoredFileName = "safe.pdf", ContentType = "application/pdf", PatientUserId = patient.Id, UploadedByUserId = patient.Id };
        db.ApplicationUsers.AddRange(patient, other); db.MedicalReports.Add(report); db.SaveChanges();
        return (db, patient, other, report);
    }

    private static ClaimsPrincipal User(ApplicationUser user) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, user.Role)], "test"));

    [Fact]
    public void PasswordHasher_does_not_store_plaintext()
    {
        var hasher = new PasswordHasher<ApplicationUser>(); var user = new ApplicationUser(); var password = "StrongPassword123!"; user.PasswordHash = hasher.HashPassword(user, password);
        Assert.NotEqual(password, user.PasswordHash); Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(user, user.PasswordHash, password));
    }

    [Fact]
    public async Task Patient_can_view_own_report_but_not_other_report()
    {
        var (db, patient, other, report) = Fixture(); var auth = new MedicalResourceAuthorizationService(db);
        Assert.True(await auth.CanViewReportAsync(User(patient), report.Id));
        Assert.False(await auth.CanViewReportAsync(User(other), report.Id));
    }

    [Fact]
    public async Task Unauthenticated_and_admin_cannot_view_clinical_report()
    {
        var (db, _, _, report) = Fixture(); var auth = new MedicalResourceAuthorizationService(db);
        Assert.False(await auth.CanViewReportAsync(new ClaimsPrincipal(new ClaimsIdentity()), report.Id));
        var admin = new ApplicationUser { Role = "ADMIN" }; Assert.False(await auth.CanViewReportAsync(User(admin), report.Id));
    }

    [Fact]
    public async Task Patient_cannot_modify_report_but_pathologist_can_with_organization_scope()
    {
        var (db, patient, _, report) = Fixture(); var auth = new MedicalResourceAuthorizationService(db);
        Assert.False(await auth.CanModifyReportAsync(User(patient), report.Id));
        var org = Guid.NewGuid(); report.OrganizationId = org; var pathologist = new ApplicationUser { Role = "PATHOLOGIST" }; db.SaveChanges();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, pathologist.Id.ToString()), new Claim(ClaimTypes.Role, pathologist.Role), new Claim("organization_id", org.ToString())], "test"));
        Assert.True(await auth.CanModifyReportAsync(principal, report.Id));
    }

    [Fact]
    public async Task Active_grant_allows_access_but_expired_and_revoked_grants_do_not()
    {
        var (db, patient, other, report) = Fixture(); var auth = new MedicalResourceAuthorizationService(db);
        db.PatientAccessGrants.Add(new PatientAccessGrant { PatientId = patient.Id, GrantedToUserId = other.Id, GrantedByUserId = patient.Id }); await db.SaveChangesAsync();
        Assert.True(await auth.CanViewReportAsync(User(other), report.Id));
        var grant = await db.PatientAccessGrants.SingleAsync(); grant.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync(); Assert.False(await auth.CanViewReportAsync(User(other), report.Id));
        grant.ExpiresAt = null; grant.RevokedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); Assert.False(await auth.CanViewReportAsync(User(other), report.Id));
    }

    [Fact]
    public async Task Audit_service_persists_safe_action_without_medical_content()
    {
        var (db, patient, _, report) = Fixture(); var audit = new SecurityAuditService(db);
        await audit.RecordAsync(User(patient), "REPORT_VIEW", true, "MedicalReport", report.Id);
        var entry = await db.SecurityAuditEvents.SingleAsync(); Assert.Equal("REPORT_VIEW", entry.Action); Assert.Equal(report.Id, entry.ResourceId); Assert.Null(entry.MetadataJson);
    }
}
