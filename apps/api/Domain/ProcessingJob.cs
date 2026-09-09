namespace AI.DocumentReader.Api.Domain;

public class ProcessingJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReportId { get; set; }
    public MedicalReport? Report { get; set; }
    public ProcessingJobStatus Status { get; set; } = ProcessingJobStatus.QUEUED;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? QueuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public DateTimeOffset? HeartbeatAt { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorSafeMessage { get; set; }
    public string ProcessingVersion { get; set; } = "13B.1";
    public string? WorkerId { get; set; }
}
