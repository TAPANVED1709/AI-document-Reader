using System.Text.Json;
using System.Text.Json.Serialization;
using AI.DocumentReader.Api.Domain;

namespace AI.DocumentReader.Api.Services;

public sealed record MedicalResultSummary(Guid Id, string TestName, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] decimal? Result, string ResultText,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Unit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ReferenceRange);
public sealed record MedicalReportSummary(Guid MedicalReportId, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ReportGeneratedDate, IReadOnlyList<MedicalResultSummary> Results);

// Read model only. LabResults remains canonical; no writes to external legacy tables.
public static class MedicalReportSummaryMapper
{
    public static string? ReportGeneratedDate(string? json)
    {
        using var data = JsonDocument.Parse(json ?? "{}");
        var root = data.RootElement;
        JsonElement date;
        if (root.TryGetProperty("report", out var report) && report.ValueKind == JsonValueKind.Object)
        {
            if (!report.TryGetProperty("reportGeneratedDate", out date)) return null;
        }
        else if (!root.TryGetProperty("reportDateIso", out date)) return null;
        return date.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed.ToString("yyyy-MM-dd") : null;
    }

    public static MedicalReportSummary Map(MedicalReport report) => new(report.Id, ReportGeneratedDate(report.StructuredDataJson),
        report.LabResults.OrderBy(r => r.PageNumber).ThenBy(r => r.CreatedAt).Select(r => new MedicalResultSummary(
            r.Id, r.CorrectedTestName ?? r.NormalizedTestName ?? r.OriginalTestName,
            r.CorrectedValueNumeric ?? r.ValueNumeric, r.CorrectedValueText ?? r.ValueText,
            r.CorrectedUnit ?? r.Unit, r.CorrectedReferenceText ?? r.ReferenceText)).ToList());
}
