import { describe, expect, it } from 'vitest';
import { filterResults } from './results';
import type { LabResult } from './types';

const result = (name: string, status: LabResult['calculatedStatus'], confidence = .9, verified = false): LabResult => ({ id: name, originalTestName: name, normalizedTestName: name, valueText: '1', calculatedStatus: status, extractionConfidence: confidence, pageNumber: 1, isVerified: verified });
const results = [result('Hemoglobin', 'LOW', .7), result('Creatinine', 'NORMAL', .9, true), result('Potassium', 'HIGH'), result('Comment', 'UNKNOWN')];

describe('result filters', () => {
  it('filters each status and search term', () => { expect(filterResults(results, 'HIGH', '')).toHaveLength(1); expect(filterResults(results, 'LOW', 'hem')).toHaveLength(1); expect(filterResults(results, 'All', 'creat')).toHaveLength(1); expect(filterResults(results, 'UNKNOWN', 'hem')).toHaveLength(0); });
  it('filters review and verified states', () => { expect(filterResults(results, 'Needs Review', '')).toHaveLength(1); expect(filterResults(results, 'Verified', '')).toHaveLength(1); });
});
