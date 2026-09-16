using AI.DocumentReader.Api.Domain;

namespace AI.DocumentReader.Api.Services;

public static class LabClassificationSafety
{
    public static bool CanClassify(LabResult row, IReadOnlyDictionary<string, decimal>? fields = null) =>
        string.IsNullOrEmpty(row.ValueOperator)
        && (fields is null ? row.ExtractionConfidence >= .8m :
            fields.GetValueOrDefault("value", row.ExtractionConfidence) >= .8m
            && fields.GetValueOrDefault("ref", 0m) >= .8m && fields.GetValueOrDefault("association", 1m) >= .8m)
        && !(row.AmbiguityReason?.Contains("source row", StringComparison.OrdinalIgnoreCase) ?? false)
        && !row.ValidationIssues.Any(i => i.Code is "LOW_VALUE_CONFIDENCE" or "LOW_REFERENCE_CONFIDENCE"
            or "ASSOCIATION_LOW_CONFIDENCE" or "MALFORMED_REFERENCE_RANGE"
            or "DUPLICATE_SOURCE_CANDIDATE" or "CONFLICTING_DUPLICATE"
            or "POSSIBLE_DECIMAL_ERROR" or "OCR_CHARACTER_CONFUSION"
            or "NUMERIC_OUT_OF_RANGE" or "NUMERIC_PRECISION_UNSUPPORTED");
}
