namespace AI.DocumentReader.Api.Domain;

public enum MedicalReportExportStatus { NOT_REQUIRED, PENDING, IN_PROGRESS, COMPLETED, FAILED }

// This ledger belongs to the canonical database, never to the external schema.
public sealed class MedicalReportExport
{
    public const string SelfUploadedDestination = "SELF_UPLOADED_REPORT_DATA";
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MedicalReportId { get; set; }
    public MedicalReport Report { get; set; } = null!;
    public string Destination { get; set; } = SelfUploadedDestination;
    public MedicalReportExportStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorSafeMessage { get; set; }
    public string? ExternalRowIdsJson { get; set; }
    public int RowCount { get; set; }
    public int SuppressedNumericCount { get; set; }
}
