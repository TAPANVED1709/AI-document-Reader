using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentReader.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/reports/{id:guid}/medical-software")]
public sealed class MedicalSoftwareExportController(DocumentDbContext db, IMedicalResourceAuthorizationService authorization, ISecurityAuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!await authorization.CanViewReportAsync(User, id, ct))
        {
            await audit.RecordAsync(User, "ACCESS_DENIED", false, "MedicalReport", id, ct);
            return NotFound(new { error = "Medical report was not found." });
        }
        var report = await db.MedicalReports.AsNoTracking().Include(x => x.LabResults).SingleAsync(x => x.Id == id, ct);
        await audit.RecordAsync(User, "MEDICAL_EXPORT_VIEW", true, "MedicalReport", id, ct);
        return Ok(SelfUploadedReportMapper.Map(report));
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(Guid id, CancellationToken ct)
    {
        if (!await authorization.CanViewReportAsync(User, id, ct))
        {
            await audit.RecordAsync(User, "ACCESS_DENIED", false, "MedicalReport", id, ct);
            return NotFound(new { error = "Medical report was not found." });
        }
        var state = await db.MedicalReportExports.AsNoTracking().Where(x => x.MedicalReportId == id)
            .Select(x => new { x.Destination, x.Status, x.AttemptCount, x.LastAttemptAt, x.NextAttemptAt, x.CompletedAt,
                x.LastErrorCode, x.LastErrorSafeMessage, x.RowCount, x.SuppressedNumericCount }).SingleOrDefaultAsync(ct);
        await audit.RecordAsync(User, "MEDICAL_EXPORT_STATUS_VIEW", true, "MedicalReport", id, ct);
        return Ok(state);
    }
}
