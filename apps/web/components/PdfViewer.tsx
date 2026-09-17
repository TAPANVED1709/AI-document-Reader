'use client';
import { useEffect, useRef, useState } from 'react';
import type { PDFDocumentProxy, PDFDocumentLoadingTask, RenderTask } from 'pdfjs-dist';
import { API_BASE_URL } from '../lib/api';

type Props = { reportId: string; page: number; setPage: (page: number) => void; sourceFileAvailable?: boolean };
export default function PdfViewer(props: Props) { return <LocalPdf key={`${props.reportId}-${props.sourceFileAvailable}`} {...props} />; }

function LocalPdf({ reportId, page, setPage, sourceFileAvailable = true }: Props) {
  const [pdf, setPdf] = useState<PDFDocumentProxy | null>(null);
  const [state, setState] = useState<'LOADING' | 'DISPLAYED' | 'SOURCE_UNAVAILABLE' | 'ERROR'>(sourceFileAvailable ? 'LOADING' : 'SOURCE_UNAVAILABLE');
  const [drawnPage, setDrawnPage] = useState(0); const [retry, setRetry] = useState(0);
  const canvas = useRef<HTMLCanvasElement>(null);
  const current = Math.min(Math.max(1, Math.trunc(page) || 1), pdf?.numPages || 1);
  useEffect(() => {
    if (!sourceFileAvailable) return;
    const abort = new AbortController(); let active = true; let task: PDFDocumentLoadingTask | undefined;
    (async () => {
      try {
        const response = await fetch(`${API_BASE_URL}/api/reports/${reportId}/file`, { credentials: 'include', cache: 'no-store', signal: abort.signal });
        if (!active) return;
        if (response.status === 404) { setState('SOURCE_UNAVAILABLE'); return; }
        if (!response.ok || !response.headers.get('content-type')?.includes('application/pdf')) throw new Error('PDF unavailable');
        const data = new Uint8Array(await response.arrayBuffer());
        const engine = await import('pdfjs-dist');
        if (!active) return;
        engine.GlobalWorkerOptions.workerSrc = '/pdfjs/pdf.worker.min.mjs';
        task = engine.getDocument({ data, enableXfa: false, cMapUrl: '/pdfjs/cmaps/', cMapPacked: true, standardFontDataUrl: '/pdfjs/standard_fonts/', wasmUrl: '/pdfjs/wasm/' });
        const loaded = await task.promise;
        if (active) setPdf(loaded);
      } catch { if (active) setState('ERROR'); }
    })();
    return () => { active = false; abort.abort(); void task?.destroy(); };
  }, [reportId, sourceFileAvailable, retry]);
  useEffect(() => {
    if (!pdf) return;
    let active = true; let render: RenderTask | undefined;
    (async () => {
      try {
        const source = await pdf.getPage(current);
        if (!active) return;
        // Offscreen rendering prevents a cancelled page painting over its replacement.
        const buffer = document.createElement('canvas');
        const natural = source.getViewport({ scale: 1 });
        const viewport = source.getViewport({ scale: Math.min(1.6, 1600 / Math.max(natural.width, natural.height)) });
        buffer.width = Math.ceil(viewport.width); buffer.height = Math.ceil(viewport.height);
        render = source.render({ canvas: buffer, viewport }); await render.promise;
        if (!active || !canvas.current) return;
        canvas.current.width = buffer.width; canvas.current.height = buffer.height;
        const context = canvas.current.getContext('2d'); if (!context) throw new Error('Canvas unavailable');
        context.drawImage(buffer, 0, 0); setDrawnPage(current); setState('DISPLAYED');
      } catch { if (active) setState('ERROR'); }
    })();
    return () => { active = false; render?.cancel(); };
  }, [pdf, current]);
  const loading = state === 'LOADING' || state === 'DISPLAYED' && drawnPage !== current;
  return <section className="flex flex-col rounded-xl border border-line bg-white p-3 shadow-sm" aria-label="Original PDF viewer">
    <div className="flex flex-wrap items-center justify-between gap-2 px-2 py-2"><h3 className="font-bold">Original PDF</h3>
      {pdf && <div className="flex items-center gap-2"><button type="button" disabled={current <= 1} onClick={() => setPage(current - 1)} className="focus-ring rounded border px-2 py-1">Previous page</button>
        <label className="text-xs">Page <input aria-label="PDF page" type="number" min="1" max={pdf.numPages} value={current} onChange={e => setPage(Math.min(pdf.numPages, Math.max(1, Math.trunc(Number(e.target.value)) || 1)))} className="w-12 rounded border border-line px-1 py-1 text-center" /> of {pdf.numPages}</label>
        <button type="button" disabled={current >= pdf.numPages} onClick={() => setPage(current + 1)} className="focus-ring rounded border px-2 py-1">Next page</button></div>}
    </div>
    {loading && <p role="status" className="p-5">Loading original report…</p>}
    {state === 'SOURCE_UNAVAILABLE' && <p role="status" className="rounded bg-slate-50 p-5">Original report file is unavailable.</p>}
    {state === 'ERROR' && <div role="alert" className="p-5"><p>Unable to display the original report. Please try again.</p><button type="button" className="focus-ring mt-3 rounded border px-3 py-2" onClick={() => { setPdf(null); setDrawnPage(0); setState('LOADING'); setRetry(r => r + 1); }}>Retry PDF</button></div>}
    <canvas ref={canvas} role="img" aria-label={`Original report page ${drawnPage}`} hidden={state !== 'DISPLAYED' || loading} className="h-auto w-full rounded border border-line" />
    {state === 'DISPLAYED' && !loading && <p role="status" className="mt-2 text-xs text-slate-600">Page {drawnPage} of {pdf?.numPages} displayed.</p>}
  </section>;
}
