import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import PdfViewer from './PdfViewer';

vi.mock('../lib/api', () => ({ API_BASE_URL: 'http://localhost:5002' }));

describe('PdfViewer', () => {
  it('embeds the authenticated file endpoint on page one without a download link', () => {
    render(<PdfViewer reportId="report-id" page={1} setPage={vi.fn()} />);
    expect(screen.getByText('Original PDF')).toBeInTheDocument();
    expect(screen.getByTitle('Original laboratory report')).toHaveAttribute('src', 'http://localhost:5002/api/reports/report-id/file#page=1');
    expect(screen.getByRole('spinbutton', { name: 'PDF page' })).toHaveValue(1);
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });

  it('updates the source page when the selected result changes', () => {
    const setPage = vi.fn();
    const { rerender } = render(<PdfViewer reportId="report-id" page={1} setPage={setPage} />);
    rerender(<PdfViewer reportId="report-id" page={3} setPage={setPage} />);
    expect(screen.getByTitle('Original laboratory report')).toHaveAttribute('src', 'http://localhost:5002/api/reports/report-id/file#page=3');
    expect(screen.getByRole('spinbutton', { name: 'PDF page' })).toHaveValue(3);
    fireEvent.change(screen.getByRole('spinbutton', { name: 'PDF page' }), { target: { value: '2' } });
    expect(setPage).toHaveBeenCalledWith(2);
  });
});
