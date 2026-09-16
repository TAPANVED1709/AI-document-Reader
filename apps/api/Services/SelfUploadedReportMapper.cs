using System.Text.Json.Serialization;
using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Infrastructure;

namespace AI.DocumentReader.Api.Services;

public sealed record SelfUploadedResultDto(string? TestName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] decimal? Result,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Unit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ReferenceRange);

public sealed record SelfUploadedReportResponseDto(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ReportGeneratedDate,
    IReadOnlyList<SelfUploadedResultDto> Results);

public static class SelfUploadedReportMapper
{
    public const decimal Maximum = 9999999999999.99999m;
    public static bool IsRepresentable(decimal value) => value >= -Maximum && value <= Maximum && decimal.Round(value, 5) == value;
    public static decimal? SafeNumeric(decimal? value) => value.HasValue && IsRepresentable(value.Value) ? value : null;

    // Export original extraction, not normalized names or a replacement for corrected clinical data.
    public static SelfUploadedReportData MapRow(LabResult row) => new()
    {
        TestName = row.OriginalTestName, Result = SafeNumeric(row.ValueNumeric), Unit = row.Unit, ReferenceRange = row.ReferenceText
    };

    public static SelfUploadedReportResponseDto Map(MedicalReport report) => new(
        MedicalReportSummaryMapper.ReportGeneratedDate(report.StructuredDataJson),
        report.LabResults.OrderBy(x => x.PageNumber).ThenBy(x => x.Id).Select(x =>
            new SelfUploadedResultDto(x.OriginalTestName, SafeNumeric(x.ValueNumeric), x.Unit, x.ReferenceText)).ToList());
}
