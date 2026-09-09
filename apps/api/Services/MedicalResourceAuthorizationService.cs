using System.Security.Claims;
using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentReader.Api.Services;

public interface IMedicalResourceAuthorizationService
{
    Task<bool> CanViewReportAsync(ClaimsPrincipal user, Guid reportId, CancellationToken ct = default);
    Task<bool> CanModifyReportAsync(ClaimsPrincipal user, Guid reportId, CancellationToken ct = default);
}

public class MedicalResourceAuthorizationService : IMedicalResourceAuthorizationService
{
    private readonly DocumentDbContext _db;
    public MedicalResourceAuthorizationService(DocumentDbContext db) => _db = db;

    public async Task<bool> CanViewReportAsync(ClaimsPrincipal user, Guid reportId, CancellationToken ct = default)
    {
        if (user.Identity?.IsAuthenticated != true) return false;
        var report = await _db.MedicalReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId, ct);
        return report is not null && Allowed(user, report);
    }

    public async Task<bool> CanModifyReportAsync(ClaimsPrincipal user, Guid reportId, CancellationToken ct = default)
    {
        if (!await CanViewReportAsync(user, reportId, ct)) return false;
        return user.IsInRole("LAB_STAFF") || user.IsInRole("PATHOLOGIST");
    }

    private bool Allowed(ClaimsPrincipal user, MedicalReport report)
    {
        if (user.IsInRole("ADMIN")) return false; // system admin does not bypass clinical ownership
        var userId = ClaimId(user, ClaimTypes.NameIdentifier);
        var organizationId = ClaimId(user, "organization_id");
        if (report.UploadedByUserId == userId || report.PatientUserId == userId || report.OrganizationId == organizationId && organizationId.HasValue) return true;
        var grants = _db.PatientAccessGrants.AsNoTracking()
            .Where(g => g.PatientId == report.PatientUserId)
            .ToList();
        return grants.Any(g =>
            (userId.HasValue && g.GrantedToUserId == userId.Value ||
             organizationId.HasValue && g.GrantedToOrganizationId == organizationId.Value) &&
            g.RevokedAt is null &&
            (!g.ExpiresAt.HasValue || g.ExpiresAt > DateTimeOffset.UtcNow));
    }

    private static Guid? ClaimId(ClaimsPrincipal user, string type) => Guid.TryParse(user.FindFirstValue(type), out var id) ? id : null;
}
