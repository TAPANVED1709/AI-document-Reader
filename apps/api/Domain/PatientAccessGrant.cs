namespace AI.DocumentReader.Api.Domain;

public class PatientAccessGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid? GrantedToUserId { get; set; }
    public Guid? GrantedToOrganizationId { get; set; }
    public string Scope { get; set; } = "READ";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid GrantedByUserId { get; set; }
}
