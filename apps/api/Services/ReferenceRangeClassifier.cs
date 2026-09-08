using AI.DocumentReader.Api.Domain;

namespace AI.DocumentReader.Api.Services;

public interface IReferenceRangeClassifier
{
    ResultStatus Classify(decimal? value, decimal? referenceMin, decimal? referenceMax, string referenceType = "UNKNOWN");
}

public class ReferenceRangeClassifier : IReferenceRangeClassifier
{
    public ResultStatus Classify(decimal? value, decimal? referenceMin, decimal? referenceMax, string referenceType = "UNKNOWN")
    {
        if (!value.HasValue || referenceType is "TEXT_ONLY") return ResultStatus.UNKNOWN;
        if (referenceType == "UNKNOWN") referenceType = referenceMin.HasValue && referenceMax.HasValue ? "BETWEEN" : referenceMax.HasValue ? "LESS_THAN_OR_EQUAL" : referenceMin.HasValue ? "GREATER_THAN_OR_EQUAL" : "UNKNOWN";
        var val = value.Value;
        return referenceType switch
        {
            "BETWEEN" when referenceMin.HasValue && referenceMax.HasValue =>
                val < referenceMin.Value ? ResultStatus.LOW : val > referenceMax.Value ? ResultStatus.HIGH : ResultStatus.NORMAL,
            "LESS_THAN" when referenceMax.HasValue => val >= referenceMax.Value ? ResultStatus.HIGH : ResultStatus.NORMAL,
            "LESS_THAN_OR_EQUAL" when referenceMax.HasValue => val > referenceMax.Value ? ResultStatus.HIGH : ResultStatus.NORMAL,
            "GREATER_THAN" when referenceMin.HasValue => val <= referenceMin.Value ? ResultStatus.LOW : ResultStatus.NORMAL,
            "GREATER_THAN_OR_EQUAL" when referenceMin.HasValue => val < referenceMin.Value ? ResultStatus.LOW : ResultStatus.NORMAL,
            _ => ResultStatus.UNKNOWN
        };
    }
}
