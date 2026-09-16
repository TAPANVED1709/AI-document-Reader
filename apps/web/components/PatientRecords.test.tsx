import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import PatientRecords from './PatientRecords';
import type { CurrentUser } from '../lib/types';
const api = vi.hoisted(() => ({ getPatientData: vi.fn(), logout: vi.fn(), getProcessingStatus: vi.fn(), uploadReport: vi.fn() }));
vi.mock('../lib/api', () => ({ ...api, API_BASE_URL: 'http://localhost:5002' }));
const user: CurrentUser = { id: 'synthetic-patient', role: 'PATIENT', email: 'synthetic@test', firstName: 'Synthetic', lastName: 'Patient' };
const page = { items: [], total: 0, page: 1, pageSize: 20 };
beforeEach(() => { vi.resetAllMocks(); api.getPatientData.mockResolvedValue(page); });
afterEach(cleanup);
it('shows loading then safe empty records with an upload action', async () => {
  render(<PatientRecords user={user} onLogout={vi.fn()} />);
  expect(screen.getAllByRole('status').length).toBeGreaterThan(0);
  expect(await screen.findByText('No medical reports uploaded yet.')).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'Upload your first report' }));
  expect(screen.getByRole('heading', { name: 'Upload Report' })).toBeTruthy();
});
it('shows a recoverable API error without leaking previous patient records', async () => {
  api.getPatientData.mockRejectedValue(new Error('Records unavailable.'));
  render(<PatientRecords user={user} onLogout={vi.fn()} />);
  expect((await screen.findAllByRole('alert'))[0].textContent).toContain('Records unavailable');
  api.getPatientData.mockResolvedValue(page);
  fireEvent.click(screen.getByRole('button', { name: 'My Medical Records' }));
  expect(await screen.findByText('No medical reports uploaded yet.')).toBeTruthy();
});
it('opens persisted qualitative and demographic-review results without staff mutation controls', async () => {
  api.getPatientData.mockImplementation(async (path: string) => path === 'medical-records/report-a' ? {
    id: 'report-a', originalFileName: 'synthetic.pdf', status: 'NEEDS_REVIEW', processingMode: 'NATIVE', resultsCount: 2,
    structuredData: { patient: { name: 'Synthetic', sex: null }, report: { laboratoryName: 'Synthetic Lab' } },
    results: [
      { id: 'q', originalTestName: 'HBsAg', valueNumeric: null, valueText: 'Non-Reactive', calculatedStatus: 'UNKNOWN', extractionConfidence: .95, pageNumber: 1, isVerified: false },
      { id: 'n', originalTestName: 'Hemoglobin (Male)', normalizedTestName: 'Hemoglobin', valueNumeric: 14, valueText: '14', unit: 'g/dL', referenceText: '13-17', calculatedStatus: 'NORMAL', applicabilityStatus: 'REVIEW_REQUIRED', applicabilityReason: 'PATIENT_SEX_MISSING', reviewRequired: true, extractionConfidence: .95, pageNumber: 1, isVerified: false }
    ]
  } : path.startsWith('medical-records?') ? { ...page, total: 1, items: [{ reportId: 'report-a', originalFileName: 'synthetic.pdf', laboratoryName: 'Synthetic Lab', reportGeneratedDate: null, uploadedAt: '2026-09-16T12:00:00Z', status: 'NEEDS_REVIEW', testCount: 2, highCount: 0, lowCount: 0, normalCount: 1, needsReviewCount: 1 }] } : page);
  render(<PatientRecords user={user} onLogout={vi.fn()} />);
  fireEvent.click(await screen.findByRole('button', { name: 'View Report' }));
  expect(await screen.findByText('Non-Reactive')).toBeTruthy();
  expect(screen.getByText('✓ NORMAL')).toBeTruthy(); expect(screen.getByText('Demographic review required')).toBeTruthy();
  expect(within(screen.getByRole('region', { name: 'Report header' })).getByText('Synthetic Lab')).toBeTruthy();
  expect(screen.getByTitle('Original laboratory report').getAttribute('src')).toContain('/report-a/file#page=1');
  expect(screen.queryByRole('button', { name: 'Verify' })).toBeNull(); expect(screen.queryByRole('button', { name: 'Correct' })).toBeNull();
});
