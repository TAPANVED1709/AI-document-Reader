namespace AI.DocumentReader.Api.Domain;

public class AnalysisRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MedicalReportId { get; set; }
    public MedicalReport? MedicalReport { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public AnalysisStatus Status { get; set; } = AnalysisStatus.Running;
    public string ProcessorVersion { get; set; } = "1.0.0";
    public string? ErrorMessage { get; set; }
}
