import { cp, mkdir } from 'node:fs/promises';
// Serve the pinned worker, fonts and character maps from this application only.
await mkdir('public/pdfjs', { recursive: true });
await cp('node_modules/pdfjs-dist/build/pdf.worker.min.mjs', 'public/pdfjs/pdf.worker.min.mjs');
for (const name of ['cmaps', 'standard_fonts', 'wasm']) await cp(`node_modules/pdfjs-dist/${name}`, `public/pdfjs/${name}`, { recursive: true });
