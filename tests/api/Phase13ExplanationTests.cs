using System.Net;
using System.Text;
using System.Text.Json;
using AI.DocumentReader.Api.Controllers;
using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;
using AI.DocumentReader.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace AI.DocumentReader.Tests;

public class Phase13ExplanationTests(ITestOutputHelper output)
{
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent("""
                {"summary":"Structured fallback","validatedFindings":[],"requiresVerification":[],"disclaimer":"Information only","provider":"deterministic","model":"template","promptVersion":"stage6-safety-v1","usedFallback":true}
                """, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task ExplanationClientSendsFastApiCamelCaseContract()
    {
        using var handler = new CaptureHandler(); using var http = new HttpClient(handler) { BaseAddress = new("http://localhost:8000") };
        var client = new AiServiceClient(http, NullLogger<AiServiceClient>.Instance);
        var result = await client.GenerateExplanationAsync(new("RESULT_EXPLANATION", new { resultCount = 1 },
            [new("Hemoglobin", 10.8m, "10.8", "g/dL", "13 - 17", "LOW", null, "AUTO_ACCEPTED", false, [], "CBC")]));
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("RESULT_EXPLANATION", json.RootElement.GetProperty("mode").GetString());
        var row = json.RootElement.GetProperty("results")[0];
        Assert.Equal("Hemoglobin", row.GetProperty("test").GetString());
        Assert.False(row.GetProperty("reviewRequired").GetBoolean());
        Assert.Equal(10.8m, row.GetProperty("value").GetDecimal());
        Assert.True(result.UsedFallback);
    }

    [Fact]
    public async Task ThreeSimultaneousExplanationsRespectTwoRequestLimit()
    {
        var options = new DbContextOptionsBuilder<DocumentDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var report = new MedicalReport { OriginalFileName = "synthetic.pdf", StoredFileName = "synthetic.pdf", ContentType = "application/pdf" };
        await using (var db = new DocumentDbContext(options)) { db.Add(report); await db.SaveChangesAsync(); }
        var auth = new Mock<IMedicalResourceAuthorizationService>();
        auth.Setup(x => x.CanViewReportAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>(), report.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var limiter = new OllamaConcurrencyLimiter(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ollama:MaxConcurrentRequests"] = "2" }).Build());
        var ai = new Mock<IAiServiceClient>(); var active = 0; var peak = 0; var calls = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ai.Setup(x => x.GenerateExplanationAsync(It.IsAny<AiExplanationRequestDto>(), It.IsAny<CancellationToken>())).Returns(async () =>
        {
            Interlocked.Increment(ref calls); var now = Interlocked.Increment(ref active);
            int prior; do { prior = peak; } while (now > prior && Interlocked.CompareExchange(ref peak, now, prior) != prior);
            try { await release.Task; return new AiExplanationResponseDto("Fallback", [], [], "Information only", "deterministic", "template", "stage6-safety-v1", true); }
            finally { Interlocked.Decrement(ref active); }
        });
        async Task<IActionResult> Request()
        { await using var db = new DocumentDbContext(options); return await new ExplanationsController(db, ai.Object, auth.Object, limiter).Overview(report.Id, default); }
        var tasks = Enumerable.Range(0, 3).Select(_ => Request()).ToArray();
        try { Assert.Equal(2, active); Assert.Equal(2, calls); }
        finally { release.SetResult(); }
        Assert.All(await Task.WhenAll(tasks), response => Assert.IsType<OkObjectResult>(response));
        Assert.Equal(2, peak); Assert.Equal(3, calls);
        output.WriteLine("Three simultaneous controller requests: peak active local explanation calls=2; completed=3");
    }
}
