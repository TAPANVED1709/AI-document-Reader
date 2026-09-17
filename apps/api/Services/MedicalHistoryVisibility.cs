using AI.DocumentReader.Api.Domain;

namespace AI.DocumentReader.Api.Services;

// Operational ingestion records remain intact. Only terminal, readable reports
// belong in clinical history; OCR-unavailable reports still need source review.
public static class MedicalHistoryVisibility
{
    public static IQueryable<MedicalReport> InMedicalHistory(this IQueryable<MedicalReport> reports) =>
        reports.Where(r => r.Status == ReportStatus.Completed || r.Status == ReportStatus.RequiresReview || r.Status == ReportStatus.RequiresOcr);

    public static IQueryable<LabResult> InMedicalHistory(this IQueryable<LabResult> results) =>
        results.Where(r => r.MedicalReport!.Status == ReportStatus.Completed || r.MedicalReport.Status == ReportStatus.RequiresReview || r.MedicalReport.Status == ReportStatus.RequiresOcr);
}
