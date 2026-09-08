using AI.DocumentReader.Api.Domain;

namespace AI.DocumentReader.Api.Services;

public interface IReferenceRangeClassifier
{
    ResultStatus Classify(decimal? value, decimal? referenceMin, decimal? referenceMax);
}

/// <summary>
/// Deterministic classification engine strictly following laboratory medicine boundaries.
/// This service NEVER manufactures reference ranges or generates medical diagnoses.
/// </summary>
public class ReferenceRangeClassifier : IReferenceRangeClassifier
{
    public ResultStatus Classify(decimal? value, decimal? referenceMin, decimal? referenceMax)
    {
        // If numeric value is missing, classification is impossible
        if (!value.HasValue)
        {
            return ResultStatus.UNKNOWN;
        }

        decimal val = value.Value;

        // Case 1: Both lower and upper bounds are defined [min, max]
        if (referenceMin.HasValue && referenceMax.HasValue)
        {
            if (val < referenceMin.Value)
            {
                return ResultStatus.LOW;
            }

            if (val > referenceMax.Value)
            {
                return ResultStatus.HIGH;
            }

            return ResultStatus.NORMAL;
        }

        // Case 2: Only upper bound is defined (e.g. "< 200")
        if (!referenceMin.HasValue && referenceMax.HasValue)
        {
            if (val > referenceMax.Value)
            {
                return ResultStatus.HIGH;
            }

            return ResultStatus.NORMAL;
        }

        // Case 3: Only lower bound is defined (e.g. "> 60")
        if (referenceMin.HasValue && !referenceMax.HasValue)
        {
            if (val < referenceMin.Value)
            {
                return ResultStatus.LOW;
            }

            return ResultStatus.NORMAL;
        }

        // Case 4: No reference bounds were extracted from the document
        return ResultStatus.UNKNOWN;
    }
}
