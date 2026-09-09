export type ExplanationResponse = { summary: string; validatedFindings: { test?: string; explanation?: string }[]; requiresVerification: { test?: string; reason?: string }[]; disclaimer: string; provider: string; model: string; promptVersion: string; usedFallback?: boolean; };
export type TrendResponse = { test: string; unit?: string; points: { reportId: string; date?: string; value?: number; valueText: string; status: Status; reviewState: string; included?: boolean; exclusionReason?: string }[]; requiresVerificationTrendPoints: { reportId: string; valueText: string; exclusionReason?: string }[]; firstValue?: number; latestValue?: number; absoluteChange?: number; percentagePointChange?: number; direction: 'INCREASED' | 'DECREASED' | 'UNCHANGED' | 'INSUFFICIENT_DATA'; validationIssues: string[]; summary?: string; };

export type Status = 'NORMAL' | 'LOW' | 'HIGH' | 'UNKNOWN';
export type Filter = 'All' | Status | 'Needs Review' | 'Verified';

export type ValidationIssue = { id?: string; code: string; severity: "INFO" | "WARNING" | "HIGH"; field?: string; message: string; requiresReview: boolean; isResolved?: boolean; resolutionType?: string; };

export type LabResult = {
  id: string;
  normalizedTestName?: string;
  originalTestName: string;
  valueNumeric?: number;
  valueText: string;
  unit?: string;
  originalUnit?: string;
  normalizedUnit?: string;
  valueOperator?: string;
  referenceType?: string;
  referenceOperator?: string;
  reportedFlag?: string;
  sectionName?: string;
  methodText?: string;
  reviewRequired?: boolean;
  ambiguityReason?: string;
  flagDiscrepancy?: string;
  reviewState?: "AUTO_ACCEPTED" | "REVIEW_REQUIRED" | "HUMAN_VERIFIED" | "HUMAN_CORRECTED";
  validationIssues?: ValidationIssue[];
  referenceMin?: number;
  referenceMax?: number;
  referenceText?: string;
  calculatedStatus: Status;
  extractionConfidence: number;
  pageNumber: number;
  sourceType?: string;
  boundingBoxJson?: string;
  isVerified: boolean;
  isCorrected?: boolean;
  correctionReason?: string;
};

export type Report = {
  id: string;
  originalFileName: string;
  processingMode: string;
  pageSources?: { page: number; source: string }[];
  results: LabResult[];
  resultsCount: number;
  reportDate?: string;
  reportDateSource?: 'REPORT_DATE' | 'COLLECTION_DATE' | 'UPLOAD_DATE' | 'UNKNOWN';
  validationSummary?: { totalResults: number; autoAccepted: number; reviewRequired: number; verified: number; corrected: number; };
  documentType?: 'LAB_REPORT' | 'DISCHARGE_SUMMARY' | 'PRESCRIPTION' | 'RADIOLOGY_REPORT' | 'UNKNOWN';
  documentTypeConfidence?: number;
  documentTypeSignals?: string[];
  structuredData?: Record<string, unknown>;
};
export type QueuedReport = { reportId: string; jobId: string; status: 'QUEUED' | 'PROCESSING' | 'COMPLETED' | 'REVIEW_REQUIRED' | 'FAILED'; };

export type TimelineEvent = { eventId: string; reportId: string; date?: string; dateSource: string; title: string; reportType: string; documentType?: string; sections: string[]; resultCount: number; reviewRequiredCount: number; verifiedCount: number; correctedCount: number; highCount: number; lowCount: number; normalCount: number; unknownCount: number; };
export type TimelineResult = { id: string; reportId: string; test: string; value?: number; valueText: string; unit?: string; date?: string; dateSource: string; status: Status; reviewState: string; included: boolean; confidence: number; section?: string; };
export type CurrentUser = { id: string; email: string; firstName: string; lastName: string; role: 'PATIENT' | 'LAB_STAFF' | 'PATHOLOGIST' | 'ADMIN'; organizationId?: string };
