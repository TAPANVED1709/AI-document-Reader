using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.DocumentReader.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI.DocumentReader.Tests;

public class BatchAcceptanceTests
{
    [Fact]
    public async Task TwoValidPdfsAreQueuedIndependentlyOfInvalidFile()
    {
        await using var factory = new Phase10HttpSecurityFactory();
        await factory.SeedAsync();
        var (client, _) = await factory.LoginAsync("lab-a@test");
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        var token = await client.GetFromJsonAsync<Phase10HttpSecurityFactory.CsrfResponse>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token!.Token);
        using var pdf = typeof(BatchAcceptanceTests).Assembly.GetManifestResourceStream("SyntheticBatch.pdf")!;
        using var bytes = new MemoryStream(); await pdf.CopyToAsync(bytes);
        using var form = new MultipartFormDataContent();
        foreach (var name in new[] { "valid-a.pdf", "valid-b.pdf", "invalid.pdf" })
        {
            var file = new ByteArrayContent(name == "invalid.pdf" ? "not a PDF"u8.ToArray() : bytes.ToArray());
            file.Headers.ContentType = new("application/pdf"); form.Add(file, "files", name);
        }
        var response = await client.PostAsync("/api/reports/batch", form);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var rows = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToArray();
        Assert.Equal(3, rows.Length);
        Assert.Equal("INVALID_PDF", rows[2].GetProperty("status").GetString());
        var ids = rows.Take(2).Select(r => r.GetProperty("reportId").GetGuid()).ToArray();
        Assert.All(rows.Take(2), r => Assert.Equal("QUEUED", r.GetProperty("status").GetString()));
        Assert.Equal(2, ids.Distinct().Count());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
        Assert.Equal(2, await db.MedicalReports.CountAsync(r => ids.Contains(r.Id)));
        Assert.Equal(2, await db.ProcessingJobs.CountAsync(j => ids.Contains(j.ReportId)));
        Assert.False(await db.MedicalReports.AnyAsync(r => r.OriginalFileName == "invalid.pdf"));
    }
}
