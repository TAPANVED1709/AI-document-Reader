namespace AI.DocumentReader.Api.Domain;

public enum ReportStatus
{
    Uploaded,
    Processing,
    Completed,
    RequiresOcr,
    RequiresReview,
    Failed
}

public enum ResultStatus
{
    NORMAL,
    LOW,
    HIGH,
    UNKNOWN
}

public enum AnalysisStatus
{
    Running,
    Completed,
    Failed
}

public enum ProcessingJobStatus
{
    QUEUED,
    PROCESSING,
    RETRYING,
    COMPLETED,
    REVIEW_REQUIRED,
    FAILED
}
