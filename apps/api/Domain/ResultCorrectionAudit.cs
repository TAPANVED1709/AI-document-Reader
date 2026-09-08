namespace AI.DocumentReader.Api.Domain;

public class ResultCorrectionAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LabResultId { get; set; }
    public LabResult? LabResult { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ChangedBy { get; set; }
}
