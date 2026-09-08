export type Status = 'NORMAL' | 'LOW' | 'HIGH' | 'UNKNOWN';
export type Filter = 'All' | Status | 'Needs Review' | 'Verified';

export type LabResult = {
  id: string;
  normalizedTestName?: string;
  originalTestName: string;
  valueNumeric?: number;
  valueText: string;
  unit?: string;
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
};
