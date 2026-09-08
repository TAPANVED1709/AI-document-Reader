using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AI.DocumentReader.Api.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    private readonly DocumentDbContext _dbContext;
    private readonly IAiServiceClient _aiServiceClient;

    public HealthController(DocumentDbContext dbContext, IAiServiceClient aiServiceClient)
    {
        _dbContext = dbContext;
        _aiServiceClient = aiServiceClient;
    }

    [HttpGet("/health")]
    public async Task<IActionResult> GetHealth(CancellationToken cancellationToken)
    {
        bool dbConnected = false;
        try
        {
            dbConnected = await _dbContext.Database.CanConnectAsync(cancellationToken);
        }
        catch
        {
            dbConnected = false;
        }

        bool aiServiceAvailable = await _aiServiceClient.CheckHealthAsync(cancellationToken);

        var healthStatus = new
        {
            status = "Healthy",
            service = "api",
            version = "1.0.0",
            timestamp = DateTimeOffset.UtcNow,
            dependencies = new
            {
                database = dbConnected ? "Connected" : "Disconnected",
                aiService = aiServiceAvailable ? "Available" : "Unavailable"
            }
        };

        return Ok(healthStatus);
    }
}
