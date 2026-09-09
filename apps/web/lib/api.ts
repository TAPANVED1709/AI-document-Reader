import type { ExplanationResponse, Report, LabResult } from './types';

export const API_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL || 'http://localhost:5000';
async function readError(response: Response) { const body = await response.json().catch(() => null); return body?.error || 'The document service could not process this report.'; }
export async function uploadReport(file: File): Promise<Report> { const form = new FormData(); form.append('file', file); const response = await fetch(`${API_BASE_URL}/api/reports/upload`, { method: 'POST', body: form }); if (!response.ok) throw new Error(await readError(response)); return response.json(); }
export async function verifyResult(reportId: string, resultId: string) { const response = await fetch(`${API_BASE_URL}/api/reports/${reportId}/results/${resultId}/verify`, { method: 'POST' }); if (!response.ok) throw new Error(await readError(response)); }
export async function correctResult(reportId: string, resultId: string, payload: Record<string, unknown>): Promise<LabResult> { const response = await fetch(`${API_BASE_URL}/api/reports/${reportId}/results/${resultId}`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) }); if (!response.ok) throw new Error(await readError(response)); return response.json(); }

export async function explainReport(reportId: string, mode: 'patient' | 'clinician'): Promise<ExplanationResponse> { const response = await fetch(API_BASE_URL + '/api/reports/' + reportId + '/explanations/' + mode, { method: 'POST' }); if (!response.ok) throw new Error(await readError(response)); return response.json(); }
export async function explainResult(reportId: string, resultId: string): Promise<ExplanationResponse> { const response = await fetch(API_BASE_URL + '/api/reports/' + reportId + '/results/' + resultId + '/explain', { method: 'POST' }); if (!response.ok) throw new Error(await readError(response)); return response.json(); }
