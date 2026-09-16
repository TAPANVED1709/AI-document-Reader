using System.Globalization;
using AI.DocumentReader.Api.Domain;

namespace AI.DocumentReader.Api.Services;

// Matches existing SQL columns. These are storage limits, not medical ranges.
public static class LabNumericSafety
{
    public const decimal ExtractedMaximum = 99999999999999.9999m;
    public const decimal CorrectedMaximum = 9999999999999999.99m;

    public static string? IssueCode(decimal? value, bool corrected = false)
    {
        if (value is null) return null;
        var maximum = corrected ? CorrectedMaximum : ExtractedMaximum;
        if (value > maximum || value < -maximum) return "NUMERIC_OUT_OF_RANGE";
        return decimal.Round(value.Value, corrected ? 2 : 4) != value
            ? "NUMERIC_PRECISION_UNSUPPORTED" : null;
    }

    // Only called for newly extracted rows; never rewrites human corrections.
    public static void SanitizeExtracted(LabResult row)
    {
        decimal? Safe(decimal? value, string field)
        {
            var code = IssueCode(value);
            if (code is null) return value;
            // Normally the parser supplies these verbatim source fields. If it
            // did not, retain the candidate as text instead of dropping evidence.
            if (field == nameof(LabResult.ValueNumeric) && string.IsNullOrWhiteSpace(row.ValueText))
                row.ValueText = value!.Value.ToString(CultureInfo.InvariantCulture);
            if (field != nameof(LabResult.ValueNumeric) && string.IsNullOrWhiteSpace(row.ReferenceText))
                row.ReferenceText = $"min={row.ReferenceMin?.ToString(CultureInfo.InvariantCulture)}; max={row.ReferenceMax?.ToString(CultureInfo.InvariantCulture)}";
            row.ValidationIssues.Add(new ValidationIssue { Code = code, Severity = "HIGH", FieldName = field,
                Message = "Extracted numeric candidate cannot be stored exactly in the supported decimal format. Verify the preserved source text.", RequiresReview = true });
            // Keep a long existing ambiguity explanation in the issue evidence.
            if (!(row.AmbiguityReason ?? "").Contains(code, StringComparison.Ordinal))
            {
                var reason = string.IsNullOrEmpty(row.AmbiguityReason) ? code : row.AmbiguityReason + "; " + code;
                if (reason.Length > 500)
                {
                    row.ValidationIssues.Add(new ValidationIssue { Code = "SOURCE_AMBIGUITY", Severity = "HIGH", Message = row.AmbiguityReason!, RequiresReview = true });
                    reason = code;
                }
                row.AmbiguityReason = reason;
            }
            return null;
        }
        row.ValueNumeric = Safe(row.ValueNumeric, nameof(LabResult.ValueNumeric));
        row.ReferenceMin = Safe(row.ReferenceMin, nameof(LabResult.ReferenceMin));
        row.ReferenceMax = Safe(row.ReferenceMax, nameof(LabResult.ReferenceMax));
        if (row.ValidationIssues.Any(i => i.Code is "NUMERIC_OUT_OF_RANGE" or "NUMERIC_PRECISION_UNSUPPORTED"))
        {
            row.ReviewRequired = true;
            row.CalculatedStatus = ResultStatus.UNKNOWN;
        }
    }
}
