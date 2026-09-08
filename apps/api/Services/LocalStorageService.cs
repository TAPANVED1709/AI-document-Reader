namespace AI.DocumentReader.Api.Services;

public interface ILocalStorageService
{
    Task<(string storedFileName, string absolutePath)> SaveReportAsync(IFormFile file, CancellationToken cancellationToken = default);
    string GetReportAbsolutePath(string storedFileName);
    bool FileExists(string storedFileName);
    void ValidateUploadFile(IFormFile file);
}

public class LocalStorageService : ILocalStorageService
{
    private readonly string _storageDirectory;
    private const long MaxFileSizeInBytes = 20 * 1024 * 1024; // 20 MB development limit

    public LocalStorageService(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configuredPath = configuration["Storage:ReportsPath"] ?? "storage/reports";

        // Resolve path relative to solution/project root if not rooted
        if (Path.IsPathRooted(configuredPath))
        {
            _storageDirectory = configuredPath;
        }
        else
        {
            // Search upwards to find storage folder or root
            var current = Directory.GetCurrentDirectory();
            var candidate = Path.Combine(current, configuredPath);

            // If running inside apps/api, traverse up to AI-document-Reader root
            if (!Directory.Exists(candidate))
            {
                var parentCandidate = Path.Combine(current, "..", "..", configuredPath);
                if (Directory.Exists(Path.GetFullPath(parentCandidate)))
                {
                    candidate = Path.GetFullPath(parentCandidate);
                }
            }

            _storageDirectory = candidate;
        }

        if (!Directory.Exists(_storageDirectory))
        {
            Directory.CreateDirectory(_storageDirectory);
        }
    }

    public void ValidateUploadFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("No file was uploaded or the uploaded file is empty.");
        }

        if (file.Length > MaxFileSizeInBytes)
        {
            throw new ArgumentException($"File size exceeds maximum allowed limit of {MaxFileSizeInBytes / (1024 * 1024)} MB.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension != ".pdf")
        {
            throw new ArgumentException($"Unsupported file format '{extension}'. Only PDF files are allowed.");
        }

        var contentType = file.ContentType.ToLowerInvariant();
        if (!contentType.Contains("pdf") && !contentType.Contains("octet-stream"))
        {
            throw new ArgumentException($"Unsupported content type '{file.ContentType}'. Expected 'application/pdf'.");
        }
    }

    public async Task<(string storedFileName, string absolutePath)> SaveReportAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        ValidateUploadFile(file);

        var fileId = Guid.NewGuid();
        var storedFileName = $"{fileId}.pdf";
        var absolutePath = Path.Combine(_storageDirectory, storedFileName);

        await using var stream = new FileStream(absolutePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await file.CopyToAsync(stream, cancellationToken);

        return (storedFileName, absolutePath);
    }

    public string GetReportAbsolutePath(string storedFileName)
    {
        return Path.Combine(_storageDirectory, Path.GetFileName(storedFileName));
    }

    public bool FileExists(string storedFileName)
    {
        return File.Exists(GetReportAbsolutePath(storedFileName));
    }
}
