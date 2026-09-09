using System.Security.Claims;
using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;

namespace AI.DocumentReader.Api.Services;

public interface ISecurityAuditService
{
    Task RecordAsync(ClaimsPrincipal? user, string action, bool success, string resourceType = "", Guid? resourceId = null, CancellationToken ct = default);
}

public class SecurityAuditService : ISecurityAuditService
{
    private readonly DocumentDbContext _db;
    public SecurityAuditService(DocumentDbContext db) => _db = db;
    public async Task RecordAsync(ClaimsPrincipal? user, string action, bool success, string resourceType = "", Guid? resourceId = null, CancellationToken ct = default)
    {
        Guid? userId = Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        _db.SecurityAuditEvents.Add(new SecurityAuditEvent { UserId = userId, Action = action, Success = success, ResourceType = resourceType, ResourceId = resourceId });
        await _db.SaveChangesAsync(ct);
    }
}
