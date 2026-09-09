let csrfToken: string | undefined;
const apiFetch = async (input: RequestInfo | URL, init: RequestInit = {}) => {
  const method = (init.method || 'GET').toUpperCase();
  if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(method) && !csrfToken) {
    const response = await fetch(API_BASE_URL + '/api/auth/csrf', { credentials: 'include' });
    if (response.ok) csrfToken = (await response.json()).token;
  }
  const headers = new Headers(init.headers);
  if (csrfToken && ['POST', 'PUT', 'PATCH', 'DELETE'].includes(method)) headers.set('X-XSRF-TOKEN', csrfToken);
  return fetch(input, { ...init, headers, credentials: 'include' });
};
import type { ExplanationResponse, Report, LabResult, TrendResponse, TimelineEvent, TimelineResult } from './types';

export const API_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL || 'http://localhost:5000';
async function readError(response: Response) { const body = await response.json().catch(() => null); return body?.error || 'The document service could not process this report.'; }
export async function uploadReport(file: File): Promise<Report> { const form = new FormData(); form.append('file', file); const response = await apiFetch(`${API_BASE_URL}/api/reports/upload`, { method: 'POST', body: form }); if (!response.ok) throw new Error(await readError(response)); return response.json(); }
export async function verifyResult(reportId: string, resultId: string) { const response = await apiFetch(`${API_BASE_URL}/api/reports/${reportId}/results/${resultId}/verify`, { method: 'POST' }); if (!response.ok) throw new Error(await readError(response)); }
export async function correctResult(reportId: string, resultId: string, payload: Record<string, unknown>): Promise<LabResult> { const response = await apiFetch(`${API_BASE_URL}/api/reports/${reportId}/results/${resultId}`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) }); if (!response.ok) throw new Error(await readError(response)); return response.json(); }

export async function explainReport(reportId: string, mode: 'patient' | 'clinician'): Promise<ExplanationResponse> { const response = await apiFetch(API_BASE_URL + '/api/reports/' + reportId + '/explanations/' + mode, { method: 'POST' }); if (!response.ok) throw new Error(await readError(response)); return response.json(); }
export async function explainResult(reportId: string, resultId: string): Promise<ExplanationResponse> { const response = await apiFetch(API_BASE_URL + '/api/reports/' + reportId + '/results/' + resultId + '/explain', { method: 'POST' }); if (!response.ok) throw new Error(await readError(response)); return response.json(); }
export async function compareTrend(reportId: string, testName: string): Promise<TrendResponse> { const response = await apiFetch(API_BASE_URL + '/api/trends/compare', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ testName, reportIds: [reportId] }) }); if (!response.ok) throw new Error(await readError(response)); return response.json(); }
export async function getTimeline(): Promise<TimelineEvent[]> { const response = await apiFetch(API_BASE_URL + '/api/timeline'); if (!response.ok) throw new Error(await readError(response)); return response.json(); }
export async function getLatestResults(): Promise<TimelineResult[]> { const response = await apiFetch(API_BASE_URL + '/api/timeline/latest-results'); if (!response.ok) throw new Error(await readError(response)); return response.json(); }
export async function login(email: string, password: string) { const response = await apiFetch(API_BASE_URL + '/api/auth/login', { method: 'POST', credentials: 'include', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password }) }); if (!response.ok) throw new Error(await readError(response)); return response.json(); }
export async function logout() { await apiFetch(API_BASE_URL + '/api/auth/logout', { method: 'POST', credentials: 'include' }); }
