using System.Net.Http.Headers;
using System.Text.Json;

namespace AI.DocumentReader.Api.Services;

public record AiValidationIssueDto(string Code, string Severity, string? Field, string Message, bool RequiresReview = true, string Source = "VALIDATION");

public record AiLabResultDto(
    string OriginalName,
    string? NormalizedName,
    decimal? Value,
    string ValueText,
    string? Unit,
    decimal? ReferenceMin,
    decimal? ReferenceMax,
    string? ReferenceText,
    int Page,
    decimal Confidence,
    string? BoundingBoxJson = null,
    string? OriginalUnit = null, string? NormalizedUnit = null, string? ValueOperator = null,
    string ReferenceType = "UNKNOWN", string? ReferenceOperator = null, string? ReportedFlag = null, string? Section = null,
    string? MethodText = null, bool ReviewRequired = false, string? AmbiguityReason = null, string? Discrepancy = null,
    List<AiValidationIssueDto>? ValidationIssues = null
);

public record AiPageSourceDto(int Page, string Source, string? Error = null);

public record AiAnalysisResponseDto(
    bool RequiresOcr,
    bool OcrApplied,
    List<int> Pages,
    List<AiLabResultDto> Results,
    string ProcessingMode = "NATIVE",
    List<AiPageSourceDto>? PageSources = null
);

public interface IAiServiceClient
{
    Task<AiAnalysisResponseDto> AnalyseDocumentAsync(string filePath, string originalFileName, CancellationToken cancellationToken = default);
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}

public class AiServiceClient : IAiServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AiServiceClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AiServiceClient(HttpClient httpClient, ILogger<AiServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check failed for Python AI service.");
            return false;
        }
    }

    public async Task<AiAnalysisResponseDto> AnalyseDocumentAsync(string filePath, string originalFileName, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"PDF file not found at path: {filePath}");
        }

        _logger.LogInformation("Sending PDF '{FileName}' to AI Service for analysis...", originalFileName);

        await using var fileStream = File.OpenRead(filePath);
        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);

        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(streamContent, "file", originalFileName);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync("/analyse", content, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to Python AI Intelligence Service.");
            throw new InvalidOperationException("The Document Intelligence service is currently unreachable.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("AI Service returned error {StatusCode}: {ErrorBody}", response.StatusCode, errorBody);
            throw new InvalidOperationException($"AI Service error ({response.StatusCode}): {errorBody}");
        }

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<AiAnalysisResponseDto>(responseStream, JsonOptions, cancellationToken);

        if (result == null)
        {
            throw new InvalidOperationException("Received empty or invalid response from AI Service.");
        }

        _logger.LogInformation("AI Service processed report: requiresOcr={RequiresOcr}, testRowsExtracted={Count}",
            result.RequiresOcr, result.Results.Count);

        return result;
    }
}
