namespace AI.DocumentReader.Api.Domain;

public enum ReportStatus
{
    Uploaded,
    Processing,
    Completed,
    RequiresOcr,
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
