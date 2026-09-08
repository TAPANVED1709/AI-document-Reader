using AI.DocumentReader.Api.Domain;
using AI.DocumentReader.Api.Services;
using Xunit;

namespace AI.DocumentReader.Tests;

public class ReferenceRangeClassifierTests
{
    private readonly ReferenceRangeClassifier _classifier = new();

    [Theory]
    // Benchmark sample rows from specification
    [InlineData(10.8, 13.0, 17.0, ResultStatus.LOW)]     // Hemoglobin 10.8 g/dL (13.0 - 17.0)
    [InlineData(7.4, 4.0, 11.0, ResultStatus.NORMAL)]    // WBC 7.4 x10³/uL (4.0 - 11.0)
    [InlineData(170.0, 200.0, 900.0, ResultStatus.LOW)]  // Vitamin B12 170 pg/mL (200 - 900)
    [InlineData(6.8, 4.0, 5.6, ResultStatus.HIGH)]       // HbA1c 6.8 % (4.0 - 5.6)
    // Range boundary conditions
    [InlineData(13.0, 13.0, 17.0, ResultStatus.NORMAL)]  // Exact minimum boundary
    [InlineData(17.0, 13.0, 17.0, ResultStatus.NORMAL)]  // Exact maximum boundary
    [InlineData(12.99, 13.0, 17.0, ResultStatus.LOW)]    // Just below minimum
    [InlineData(17.01, 13.0, 17.0, ResultStatus.HIGH)]   // Just above maximum
    public void Classify_StandardInterval_ReturnsExpectedStatus(
        double value, double min, double max, ResultStatus expected)
    {
        var actual = _classifier.Classify((decimal)value, (decimal)min, (decimal)max);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(150.0, null, 200.0, ResultStatus.NORMAL)] // Within upper bound (e.g. < 200)
    [InlineData(200.0, null, 200.0, ResultStatus.NORMAL)] // Equal to upper bound
    [InlineData(220.0, null, 200.0, ResultStatus.HIGH)]   // Exceeds upper bound
    public void Classify_UpperBoundOnly_ReturnsExpectedStatus(
        double value, double? min, double? max, ResultStatus expected)
    {
        var actual = _classifier.Classify(
            (decimal)value,
            min.HasValue ? (decimal)min.Value : null,
            max.HasValue ? (decimal)max.Value : null);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(45.0, 60.0, null, ResultStatus.LOW)]     // Below lower bound (e.g. > 60)
    [InlineData(60.0, 60.0, null, ResultStatus.NORMAL)]  // Equal to lower bound
    [InlineData(75.0, 60.0, null, ResultStatus.NORMAL)]  // Above lower bound
    public void Classify_LowerBoundOnly_ReturnsExpectedStatus(
        double value, double? min, double? max, ResultStatus expected)
    {
        var actual = _classifier.Classify(
            (decimal)value,
            min.HasValue ? (decimal)min.Value : null,
            max.HasValue ? (decimal)max.Value : null);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Classify_MissingValue_ReturnsUnknown()
    {
        var actual = _classifier.Classify(null, 10.0m, 20.0m);
        Assert.Equal(ResultStatus.UNKNOWN, actual);
    }

    [Fact]
    public void Classify_MissingBothBounds_ReturnsUnknown()
    {
        var actual = _classifier.Classify(15.5m, null, null);
        Assert.Equal(ResultStatus.UNKNOWN, actual);
    }

    [Fact]
    public void Classify_AllNulls_ReturnsUnknown()
    {
        var actual = _classifier.Classify(null, null, null);
        Assert.Equal(ResultStatus.UNKNOWN, actual);
    }
}
