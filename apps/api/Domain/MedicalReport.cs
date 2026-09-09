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
    public DateTimeOffset? ReportDate { get; set; }
    public string ReportDateSource { get; set; } = "UPLOAD_DATE";
    public string DocumentType { get; set; } = "UNKNOWN";
    public decimal DocumentTypeConfidence { get; set; }
    public string? DocumentTypeSignalsJson { get; set; }
    public string? StructuredDataJson { get; set; }

    public bool OcrRequired { get; set; }
    public bool OcrApplied { get; set; }
    public string ProcessingMode { get; set; } = "NATIVE";
    public string? PageSourcesJson { get; set; }

    // Navigation collections
    public ICollection<LabResult> LabResults { get; set; } = new List<LabResult>();
    public ICollection<AnalysisRun> AnalysisRuns { get; set; } = new List<AnalysisRun>();
}
