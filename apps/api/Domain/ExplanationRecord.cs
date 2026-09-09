namespace AI.DocumentReader.Api.Domain;

public class ExplanationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MedicalReportId { get; set; }
    public Guid? LabResultId { get; set; }
    public string Mode { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public string GeneratedText { get; set; } = string.Empty;
    public string GeneratedJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
