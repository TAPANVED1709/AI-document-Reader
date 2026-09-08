import { fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { describe, expect, it, vi } from 'vitest';
import UploadZone from './UploadZone';

describe('UploadZone', () => {
  it('renders the upload workflow', () => { render(<UploadZone onUpload={vi.fn()} />); expect(screen.getByText('Drag & drop your PDF here')).toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Browse PDF' })).toBeInTheDocument(); });
  it('rejects non-PDF files before upload', () => { const onUpload = vi.fn(); render(<UploadZone onUpload={onUpload} />); const input = screen.getByLabelText('PDF file') as HTMLInputElement; fireEvent.change(input, { target: { files: [new File(['text'], 'notes.txt', { type: 'text/plain' })] } }); expect(screen.getByRole('alert')).toHaveTextContent('PDF'); expect(onUpload).not.toHaveBeenCalled(); });
});
