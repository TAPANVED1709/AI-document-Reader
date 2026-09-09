namespace AI.DocumentReader.Api.Services;

public sealed class OllamaConcurrencyLimiter
{
    public SemaphoreSlim Gate { get; }
    public OllamaConcurrencyLimiter(IConfiguration configuration)
        => Gate = new SemaphoreSlim(Math.Clamp(configuration.GetValue("Ollama:MaxConcurrentRequests", 2), 1, 8));
}
