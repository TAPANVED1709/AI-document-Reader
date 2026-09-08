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
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
