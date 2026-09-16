import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { dateLabel, trendSeries, type Measurement } from './patient-records';
import { PatientTrendChart } from '../components/PatientRecords';
afterEach(cleanup);
const point = (id: string, value: number, unit: string | null, reportDate: string | null = '2026-05-01'): Measurement => ({ id, reportId: `report-${id}`, testName: 'Creatinine', originalTestName: 'Serum Creatinine', value, unit, reportDate, uploadedAt: '2026-09-16T12:00:00Z', laboratoryName: 'Synthetic Lab', referenceRange: '0.7-1.3', status: 'NORMAL' });
describe('patient history display safety', () => {
  it('labels unavailable report dates as uploaded dates', () => { expect(dateLabel(null, '2026-09-16T12:00:00Z')).toMatch(/^Uploaded /); expect(dateLabel('2026-05-01', '2026-09-16T12:00:00Z')).toBe('1 May 2026'); });
  it('separates incompatible units without conversions and excludes unplottable data', () => {
    const result = trendSeries([point('a', 1.1, 'mg/dL'), point('b', 97, 'µmol/L'), point('c', 5, null), point('d', 2, 'mg/dL', null), point('e', NaN, 'mg/dL')]);
    expect(result.map(g => [g.unit, g.points.map(p => p.value)])).toEqual([['mg/dL', [1.1]], ['µmol/L', [97]]]);
  });
  it('renders distinct unit charts and keyboard-accessible source details', () => {
    const open = vi.fn(); render(<PatientTrendChart name="Creatinine" points={[point('a', .9, 'mg/dL'), point('b', 1.1, 'mg/dL', '2026-09-01'), point('c', 97, 'µmol/L')]} open={open} />);
    expect(screen.getAllByTestId('unit-series')).toHaveLength(1);
    expect(screen.getByText(/Unit changed/)).toBeTruthy();
    fireEvent.keyDown(screen.getByRole('button', { name: '2026-05-01: 0.9 mg/dL, Serum Creatinine' }), { key: 'Enter' });
    expect(screen.getByLabelText('Selected measurement').textContent).toContain('Printed reference: 0.7-1.3');
    fireEvent.click(screen.getByRole('button', { name: 'View source report' })); expect(open).toHaveBeenCalledWith('report-a');
  });
  it('does not fabricate points or a normal band for undated data', () => {
    render(<PatientTrendChart name="Creatinine" points={[point('a', .9, 'mg/dL', null)]} open={vi.fn()} />);
    expect(screen.queryByRole('group')).toBeNull(); expect(screen.getByText(/not plotted/)).toBeTruthy();
  });
});
