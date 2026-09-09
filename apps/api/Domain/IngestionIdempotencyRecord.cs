namespace AI.DocumentReader.Api.Domain;

public class IngestionIdempotencyRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Key { get; set; } = string.Empty;
    public Guid ReportId { get; set; }
    public Guid JobId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
