namespace AI.DocumentReader.Api.Domain;

public class ValidationIssue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LabResultId { get; set; }
    public LabResult? LabResult { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = "WARNING";
    public string? FieldName { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool RequiresReview { get; set; }
    public bool IsResolved { get; set; }
    public string? ResolutionType { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
}
