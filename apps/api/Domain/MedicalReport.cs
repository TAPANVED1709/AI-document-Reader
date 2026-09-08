namespace AI.DocumentReader.Api.Domain;

public class MedicalReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Uploaded;
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AnalysedAt { get; set; }

    // Navigation collections
    public ICollection<LabResult> LabResults { get; set; } = new List<LabResult>();
    public ICollection<AnalysisRun> AnalysisRuns { get; set; } = new List<AnalysisRun>();
}
