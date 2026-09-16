import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import ReportHeader from './ReportHeader';
import type { Report } from '../lib/types';

const report: Report = { id: 'synthetic', originalFileName: 'synthetic.pdf', processingMode: 'NATIVE', results: [], resultsCount: 0, documentType: 'LAB_REPORT', structuredData: {
  patient: { name: 'Example Patient', patientId: 'P123', age: 22, ageUnit: 'YEARS', sex: 'FEMALE', dateOfBirth: '2004-03-07' },
  report: { laboratoryName: 'Synthetic Laboratory', reportNumber: 'R123', bookingCode: 'B123', accessionNumber: 'A123', reportGeneratedDate: '2026-09-16', bookingDate: '2026-09-14', collectionDate: '2026-09-15', collectionTime: '08:20', referringDoctor: 'Dr Example', specimenType: 'Serum', pageCount: 4 },
} };
describe('persisted report header', () => {
  it('renders every patient and report field from the API metadata', () => {
    render(<ReportHeader report={report} />);
    const header=within(screen.getByRole('region',{name:'Report header'}));
    for(const value of ['Example Patient','P123','22 years','FEMALE','2004-03-07','Synthetic Laboratory','R123','B123','A123','2026-09-16','2026-09-14','2026-09-15','08:20','Dr Example','Serum','4']) expect(header.getByText(value)).toBeInTheDocument();
  });
  it('shows Not provided without substituting timeline or upload dates', () => {
    render(<ReportHeader report={{...report,reportDate:'2026-09-16',structuredData:{patient:{},report:{}}}} />);
    expect(screen.getAllByText('Not provided')).toHaveLength(16);
    expect(screen.queryByText('2026-09-16')).not.toBeInTheDocument();
  });
  it('keeps ambiguous sex absent and an unspecified age unit unspecified', () => {
    render(<ReportHeader report={{...report,structuredData:{patient:{age:22,sex:null},report:{}}}} />);
    expect(screen.getByText('22')).toBeInTheDocument();
    expect(screen.queryByText(/years/)).not.toBeInTheDocument();
    expect(screen.queryByText('FEMALE')).not.toBeInTheDocument();
  });
  it('can display existing flat metadata without inventing a generated date', () => {
    render(<ReportHeader report={{...report,structuredData:{patientName:'Legacy Patient',bookingDate:'14 Sep 2026'}}} />);
    expect(screen.getByText('Legacy Patient')).toBeInTheDocument();
    expect(screen.getByText('14 Sep 2026')).toBeInTheDocument();
    expect(screen.getAllByText('Not provided')).toHaveLength(14);
  });
});
