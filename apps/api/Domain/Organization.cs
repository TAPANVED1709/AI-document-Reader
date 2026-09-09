namespace AI.DocumentReader.Api.Domain;

public class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "PATHOLOGY_LAB";
    public bool IsActive { get; set; } = true;
}
