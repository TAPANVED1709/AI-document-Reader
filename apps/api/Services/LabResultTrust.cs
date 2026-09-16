using AI.DocumentReader.Api.Domain;

namespace AI.DocumentReader.Api.Services;

public static class LabResultTrust
{
    public static string ReviewState(LabResult row) => row.IsVerified ? "HUMAN_VERIFIED"
        : row.CorrectedAt.HasValue ? "HUMAN_CORRECTED" : row.ReviewRequired ? "REVIEW_REQUIRED" : "AUTO_ACCEPTED";

    public static bool IsTrusted(LabResult row) => row.ApplicabilityStatus != "NOT_APPLICABLE"
        && (row.IsVerified || (!row.ReviewRequired && row.ApplicabilityStatus is "NOT_REQUIRED" or "APPLICABLE"))
        && (row.CorrectedValueNumeric ?? row.ValueNumeric).HasValue
        && LabNumericSafety.IssueCode(row.CorrectedValueNumeric ?? row.ValueNumeric, row.CorrectedValueNumeric.HasValue) is null;

    public static HashSet<Guid> ConflictingDemographicRows(IEnumerable<LabResult> rows) => rows.Where(IsTrusted)
        .GroupBy(r => (r.MedicalReportId, Name(r)))
        .Where(g => g.Select(r => r.DemographicQualifier).Distinct().Count() > 1)
        .SelectMany(g => g.Select(r => r.Id)).ToHashSet();

    public static string Name(LabResult row) => new((row.CorrectedTestName ?? row.NormalizedTestName ?? row.OriginalTestName)
        .Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
