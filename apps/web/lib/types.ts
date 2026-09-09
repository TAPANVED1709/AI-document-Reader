export type ExplanationResponse = { summary: string; validatedFindings: { test?: string; explanation?: string }[]; requiresVerification: { test?: string; reason?: string }[]; disclaimer: string; provider: string; model: string; promptVersion: string; usedFallback?: boolean; };

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
  validationSummary?: { totalResults: number; autoAccepted: number; reviewRequired: number; verified: number; corrected: number; };
};
