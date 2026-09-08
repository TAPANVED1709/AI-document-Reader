using AI.DocumentReader.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace AI.DocumentReader.Tests;

public class UploadValidationTests
{
    private readonly LocalStorageService _storageService;

    public UploadValidationTests()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"Storage:ReportsPath", Path.Combine(Path.GetTempPath(), "test_reports")}
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(Path.GetTempPath());

        _storageService = new LocalStorageService(configuration, mockEnv.Object);
    }

    private static IFormFile CreateMockFile(string fileName, string contentType, long sizeBytes)
    {
        var stream = new MemoryStream(new byte[sizeBytes]);
        return new FormFile(stream, 0, sizeBytes, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    [Fact]
    public void ValidateUploadFile_ValidPdf_Succeeds()
    {
        var file = CreateMockFile("report.pdf", "application/pdf", 1024 * 50); // 50 KB
        var exception = Record.Exception(() => _storageService.ValidateUploadFile(file));
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateUploadFile_EmptyFile_ThrowsArgumentException()
    {
        var file = CreateMockFile("empty.pdf", "application/pdf", 0);
        var ex = Assert.Throws<ArgumentException>(() => _storageService.ValidateUploadFile(file));
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateUploadFile_NonPdfExtension_ThrowsArgumentException()
    {
        var file = CreateMockFile("notes.docx", "application/pdf", 1024);
        var ex = Assert.Throws<ArgumentException>(() => _storageService.ValidateUploadFile(file));
        Assert.Contains("Only PDF", ex.Message);
    }

    [Fact]
    public void ValidateUploadFile_InvalidContentType_ThrowsArgumentException()
    {
        var file = CreateMockFile("report.pdf", "image/png", 1024);
        var ex = Assert.Throws<ArgumentException>(() => _storageService.ValidateUploadFile(file));
        Assert.Contains("application/pdf", ex.Message);
    }

    [Fact]
    public void ValidateUploadFile_OversizedFile_ThrowsArgumentException()
    {
        long twentyFiveMb = 25 * 1024 * 1024;
        var file = CreateMockFile("large.pdf", "application/pdf", twentyFiveMb);
        var ex = Assert.Throws<ArgumentException>(() => _storageService.ValidateUploadFile(file));
        Assert.Contains("maximum allowed limit", ex.Message);
    }
}
