using AI.DocumentReader.Api.Controllers;
using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AI.DocumentReader.Tests;

public class HealthControllerTests
{
    [Fact]
    public async Task GetHealth_ReturnsOkResultWithStatus()
    {
        var options = new DbContextOptionsBuilder<DocumentDbContext>()
            .UseInMemoryDatabase(databaseName: "HealthTestDb")
            .Options;
        var dbContext = new DocumentDbContext(options);

        var mockAiService = new Mock<IAiServiceClient>();
        mockAiService.Setup(s => s.CheckHealthAsync(It.IsAny<CancellationToken>()))
                      .ReturnsAsync(true);

        var controller = new HealthController(dbContext, mockAiService.Object);

        var result = await controller.GetHealth(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }
}
