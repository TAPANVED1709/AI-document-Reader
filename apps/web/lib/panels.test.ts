import { describe, expect, it } from 'vitest';
import { groupPathologyResults, pathologyPanel } from './panels';
import type { LabResult } from './types';

const result = (name: string, sectionName?: string, reviewRequired = false): LabResult => ({ id: name, originalTestName: name, normalizedTestName: name, valueText: '1', calculatedStatus: 'NORMAL', extractionConfidence: .9, pageNumber: 1, isVerified: false, sectionName, reviewRequired });

describe('pathology panel grouping', () => {
  it.each([['Hemoglobin', 'CBC'], ['ALT', 'Liver'], ['Creatinine', 'Kidney'], ['HbA1c', 'Diabetes'], ['LDL', 'Lipids'], ['TSH', 'Thyroid'], ['Vitamin D', 'Iron / Vitamins'], ['Urine Protein', 'Urine'], ['Unknown Test', 'Other']] as const)('%s maps to %s', (name, panel) => expect(pathologyPanel(result(name))).toBe(panel));
  it('prefers a trustworthy section and hides empty groups', () => {
    const groups = groupPathologyResults([result('ALT', 'Liver Function Test'), result('Creatinine', 'Kidney Function Test')]);
    expect(groups.map(group => group.panel)).toEqual(['Liver', 'Kidney']);
  });
  it('keeps review state available for combined panel filtering', () => {
    const groups = groupPathologyResults([result('Hemoglobin', 'CBC', true), result('ALT', 'Liver')]);
    const reviewInCbc = groups.find(group => group.panel === 'CBC')?.results.filter(item => item.reviewRequired);
    expect(reviewInCbc).toHaveLength(1);
    expect(groups.find(group => group.panel === 'Liver')?.results).toHaveLength(1);
  });
});
