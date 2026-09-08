namespace AI.DocumentReader.Api.Domain;

public class LabResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MedicalReportId { get; set; }
    public MedicalReport? MedicalReport { get; set; }

    public string OriginalTestName { get; set; } = string.Empty;
    public string? NormalizedTestName { get; set; }
    public decimal? ValueNumeric { get; set; }
    public string ValueText { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public decimal? ReferenceMin { get; set; }
    public decimal? ReferenceMax { get; set; }
    public string? ReferenceText { get; set; }
    public ResultStatus CalculatedStatus { get; set; } = ResultStatus.UNKNOWN;
    public decimal ExtractionConfidence { get; set; }
    public int PageNumber { get; set; } = 1;
    public string? BoundingBoxJson { get; set; }
    public bool IsVerified { get; set; } = false;
    public string? CorrectedTestName { get; set; }
    public decimal? CorrectedValueNumeric { get; set; }
    public string? CorrectedValueText { get; set; }
    public string? CorrectedUnit { get; set; }
    public decimal? CorrectedReferenceMin { get; set; }
    public decimal? CorrectedReferenceMax { get; set; }
    public string? CorrectedReferenceText { get; set; }
    public string? CorrectionReason { get; set; }
    public DateTimeOffset? CorrectedAt { get; set; }
    public string? CorrectedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
