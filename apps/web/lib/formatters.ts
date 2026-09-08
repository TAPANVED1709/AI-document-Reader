export const modeLabel = (mode: string) => mode === 'OCR' ? 'Local OCR' : mode === 'HYBRID' ? 'Hybrid' : 'Native PDF';
export const statusIcon = { NORMAL: '✓', LOW: '↓', HIGH: '↑', UNKNOWN: '?' } as const;
export const displayValue = (value?: number, text?: string) => value === undefined || value === null ? text || '—' : String(value);
