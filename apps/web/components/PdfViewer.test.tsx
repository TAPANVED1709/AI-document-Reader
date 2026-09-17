import React from 'react';
import { fireEvent, render, screen, cleanup, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import PdfViewer from './PdfViewer';
const mocks=vi.hoisted(()=>({getPage:vi.fn(),destroy:vi.fn(),getDocument:vi.fn()}));
vi.mock('pdfjs-dist',()=>({GlobalWorkerOptions:{},getDocument:mocks.getDocument}));
vi.mock('../lib/api',()=>({API_BASE_URL:'http://localhost:5002'}));
beforeEach(()=>{
  vi.clearAllMocks(); vi.stubGlobal('fetch',vi.fn().mockResolvedValue({ok:true,status:200,headers:new Headers({'content-type':'application/pdf'}),arrayBuffer:async()=>new ArrayBuffer(8)}));
  vi.spyOn(HTMLCanvasElement.prototype,'getContext').mockReturnValue({drawImage:vi.fn()} as unknown as CanvasRenderingContext2D);
  mocks.getPage.mockResolvedValue({getViewport:()=>({width:600,height:800}),render:()=>({promise:Promise.resolve(),cancel:vi.fn()})});
  mocks.getDocument.mockReturnValue({promise:Promise.resolve({numPages:3,getPage:mocks.getPage}),destroy:mocks.destroy});
});
afterEach(()=>{cleanup();vi.restoreAllMocks();vi.unstubAllGlobals();});
it('fetches authenticated PDF and displays only after rendering completes',async()=>{
  render(<PdfViewer reportId="one" page={1} setPage={vi.fn()} />);
  expect(screen.getByRole('status')).toHaveTextContent('Loading');
  expect(await screen.findByText('Page 1 of 3 displayed.')).toBeTruthy();
  expect(fetch).toHaveBeenCalledWith('http://localhost:5002/api/reports/one/file',expect.objectContaining({credentials:'include',cache:'no-store'}));
  expect(mocks.getDocument).toHaveBeenCalledWith(expect.objectContaining({enableXfa:false,cMapUrl:'/pdfjs/cmaps/',standardFontDataUrl:'/pdfjs/standard_fonts/',wasmUrl:'/pdfjs/wasm/'}));
});
it('renders the selected source page and bounds navigation',async()=>{
  const setPage=vi.fn();const {rerender}=render(<PdfViewer reportId="one" page={1} setPage={setPage} />);
  await screen.findByText('Page 1 of 3 displayed.');rerender(<PdfViewer reportId="one" page={3} setPage={setPage} />);
  await screen.findByText('Page 3 of 3 displayed.');expect(mocks.getPage).toHaveBeenLastCalledWith(3);
  expect(screen.getByRole('button',{name:'Next page'})).toBeDisabled();
  fireEvent.click(screen.getByRole('button',{name:'Previous page'}));expect(setPage).toHaveBeenCalledWith(2);
});
it('shows source unavailable without fetching when DTO reports missing source',()=>{
  render(<PdfViewer reportId="missing" page={1} setPage={vi.fn()} sourceFileAvailable={false} />);
  expect(screen.getByText('Original report file is unavailable.')).toBeTruthy();expect(fetch).not.toHaveBeenCalled();
});
it('handles a missing file after DTO retrieval',async()=>{
  vi.mocked(fetch).mockResolvedValue({status:404} as Response);render(<PdfViewer reportId="one" page={1} setPage={vi.fn()} />);
  expect(await screen.findByText('Original report file is unavailable.')).toBeTruthy();
});
it('shows a safe error and retries a failed request',async()=>{
  vi.mocked(fetch).mockRejectedValueOnce(new Error('internal path'));render(<PdfViewer reportId="one" page={1} setPage={vi.fn()} />);
  expect(await screen.findByRole('alert')).not.toHaveTextContent('internal path');fireEvent.click(screen.getByRole('button',{name:'Retry PDF'}));
  await screen.findByText('Page 1 of 3 displayed.');
});
it('destroys the document and fetch state on report change',async()=>{
  const {rerender}=render(<PdfViewer reportId="one" page={1} setPage={vi.fn()} />);await screen.findByText('Page 1 of 3 displayed.');
  rerender(<PdfViewer reportId="two" page={1} setPage={vi.fn()} />);await waitFor(()=>expect(mocks.destroy).toHaveBeenCalled());
});
