import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import ResultStatus from './ResultStatus';
import { filterResults } from '../lib/results';
import type { LabResult } from '../lib/types';

const row: LabResult = { id: 'synthetic', originalTestName: 'Serum Creatinine (Male)', normalizedTestName: 'Creatinine', valueNumeric: .7, valueText: '0.7', unit: 'mg/dL', calculatedStatus: 'NORMAL', extractionConfidence: .7, pageNumber: 1, isVerified: false, reviewRequired: true, demographicQualifier: 'MALE', applicabilityStatus: 'REVIEW_REQUIRED', applicabilityReason: 'PATIENT_SEX_MISSING' };
describe('independent pathology status', () => {
  it('shows numeric NORMAL with demographic review even at lower aggregate confidence', () => {
    render(<ResultStatus result={row} />);
    expect(screen.getByText(/NORMAL/)).toBeInTheDocument();
    expect(screen.getByText('Demographic review required')).toBeInTheDocument();
    expect(screen.getByText('Reason: Patient sex not available')).toBeInTheDocument();
    expect(screen.queryByText(/UNKNOWN/)).not.toBeInTheDocument();
    expect(filterResults([row], 'NORMAL', '')).toHaveLength(1);
    expect(filterResults([row], 'Needs Review', '')).toHaveLength(1);
  });
  it('labels genuinely unresolved results NEEDS REVIEW', () => {
    const unresolved = { ...row, calculatedStatus: 'UNKNOWN' as const, applicabilityStatus: 'NOT_REQUIRED' as const, reviewRequired: false, extractionConfidence: 1 };
    render(<ResultStatus result={unresolved} />);
    expect(screen.getByText('NEEDS REVIEW')).toBeInTheDocument();
    expect(screen.queryByText(/UNKNOWN/)).not.toBeInTheDocument();
    expect(filterResults([unresolved], 'Needs Review', '')).toHaveLength(1);
  });
  it('shows non-applicability independently from HIGH', () => {
    render(<ResultStatus result={{ ...row, calculatedStatus: 'HIGH', applicabilityStatus: 'NOT_APPLICABLE', applicabilityReason: 'PATIENT_SEX_MISMATCH' }} />);
    expect(screen.getByText(/HIGH/)).toBeInTheDocument();
    expect(screen.getByText('Not applicable to current patient demographics')).toBeInTheDocument();
  });
  it('does not warn about demographics when they apply', () => {
    render(<ResultStatus result={{ ...row, applicabilityStatus: 'APPLICABLE', applicabilityReason: null, reviewRequired: false }} />);
    expect(screen.queryByText(/Demographic review/)).not.toBeInTheDocument();
    expect(screen.getByText(/NORMAL/)).toBeInTheDocument();
  });
});
