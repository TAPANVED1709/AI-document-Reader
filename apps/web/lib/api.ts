let csrfToken: string | undefined;
let csrfGeneration = 0;
let csrfRequest: Promise<string> | undefined;
const clearCsrfToken = () => {
  csrfToken = undefined;
  csrfRequest = undefined;
  csrfGeneration++;
};
const getCsrfToken = async (): Promise<string> => {
  if (csrfToken) return csrfToken;
  if (csrfRequest) return csrfRequest;
  const generation = csrfGeneration;
  const request = (async () => {
    const response = await fetch(API_BASE_URL + '/api/auth/csrf', { credentials: 'include', cache: 'no-store' });
    const body = response.ok ? await response.json() : null;
    if (typeof body?.token !== 'string' || !body.token) throw new Error('Could not obtain a CSRF token. Please try again.');
    // An issuance started before login/logout must not repopulate the cache.
    if (generation !== csrfGeneration) return getCsrfToken();
    csrfToken = body.token;
    return csrfToken!;
  })();
  csrfRequest = request;
  try { return await request; }
  finally { if (csrfRequest === request) csrfRequest = undefined; }
};
const apiFetch = async (input: RequestInfo | URL, init: RequestInit = {}) => {
  const method = (init.method || 'GET').toUpperCase();
  if (!['POST', 'PUT', 'PATCH', 'DELETE'].includes(method)) return fetch(input, { ...init, credentials: 'include' });
  for (let attempt = 0; ; attempt++) {
    const token = await getCsrfToken();
    const headers = new Headers(init.headers);
    headers.set('X-XSRF-TOKEN', token);
    const response = await fetch(input, { ...init, headers, credentials: 'include' });
    const error = response.status === 400 ? await response.clone().json().catch(() => null) : null;
    // Only retry a rejection from antiforgery middleware, before any action ran.
    if (attempt !== 0 || error?.error !== 'A valid CSRF token is required.') return response;
    if (csrfToken === token) clearCsrfToken();
  }
};
import type { CurrentUser, ExplanationResponse, Report, QueuedReport, LabResult, TrendResponse, TimelineEvent, TimelineResult } from './types';

export const API_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL || 'http://localhost:5002';
export async function getPatientData<T>(path: string): Promise<T> {
  const response = await apiFetch(`${API_BASE_URL}/api/patient/${path}`, { cache: 'no-store' });
  if (!response.ok) throw new Error(await readError(response));
  return response.json();
}
async function readError(response: Response) { const body = await response.json().catch(() => null); return body?.error || 'The document service could not process this report.'; }
export async function uploadReport(file: File, selfUpload = false): Promise<Report | QueuedReport> { const form = new FormData(); form.append('file', file); const response = await apiFetch(`${API_BASE_URL}/api/reports/${selfUpload ? 'self-upload' : 'upload'}`, { method: 'POST', body: form }); if (!response.ok) throw new Error(await readError(response)); return response.json(); }
export async function getCurrentUser(): Promise<CurrentUser | null> { const response = await apiFetch(`${API_BASE_URL}/api/auth/me`); if (response.status === 401) return null; if (!response.ok) throw new Error(await readError(response)); return response.json(); }
export async function getReport(reportId: string): Promise<Report> { const response = await apiFetch(`${API_BASE_URL}/api/reports/${reportId}`); if (!response.ok) throw new Error(await readError(response)); return response.json(); }
export async function getProcessingStatus(reportId: string, selfUpload = false) { const response = await apiFetch(`${API_BASE_URL}/api/reports/${reportId}/${selfUpload ? 'self-processing-status' : 'processing-status'}`); if (!response.ok) throw new Error(await readError(response)); return response.json() as Promise<{ reportId: string; jobId: string; status: string; safeErrorCode?: string }>; }
export async function getReviewQueue() { const response = await apiFetch(`${API_BASE_URL}/api/review-queue`); if (!response.ok) throw new Error(await readError(response)); return response.json() as Promise<{ reportId: string; fileName: string; uploadedAt: string; reportDate?: string; panel: string; issueCount: number; highestExtractionSeverity: string; processingMode: string; status: string }[]>; }
export async function verifyResult(reportId: string, resultId: string) { const response = await apiFetch(`${API_BASE_URL}/api/reports/${reportId}/results/${resultId}/verify`, { method: 'POST' }); if (!response.ok) throw new Error(await readError(response)); }
export async function correctResult(reportId: string, resultId: string, payload: Record<string, unknown>): Promise<LabResult> { const response = await apiFetch(`${API_BASE_URL}/api/reports/${reportId}/results/${resultId}`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) }); if (!response.ok) throw new Error(await readError(response)); return response.json(); }

export async function explainReport(reportId: string, mode: 'patient' | 'clinician'): Promise<ExplanationResponse> { const response = await apiFetch(API_BASE_URL + '/api/reports/' + reportId + '/explanations/' + mode, { method: 'POST' }); if (!response.ok) throw new Error(await readError(response)); return response.json(); }
export async function explainResult(reportId: string, resultId: string): Promise<ExplanationResponse> { const response = await apiFetch(API_BASE_URL + '/api/reports/' + reportId + '/results/' + resultId + '/explain', { method: 'POST' }); if (!response.ok) throw new Error(await readError(response)); return response.json(); }
export async function compareTrend(reportId: string, testName: string): Promise<TrendResponse> { const response = await apiFetch(API_BASE_URL + '/api/trends/compare', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ testName, reportIds: [reportId] }) }); if (!response.ok) throw new Error(await readError(response)); return response.json(); }
export async function getTimeline(): Promise<TimelineEvent[]> { const response = await apiFetch(API_BASE_URL + '/api/timeline'); if (!response.ok) throw new Error(await readError(response)); return response.json(); }
export async function getLatestResults(): Promise<TimelineResult[]> { const response = await apiFetch(API_BASE_URL + '/api/timeline/latest-results'); if (!response.ok) throw new Error(await readError(response)); return response.json(); }
export async function login(email: string, password: string) {
  try {
    const response = await apiFetch(API_BASE_URL + '/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password }) });
    if (!response.ok) throw new Error(await readError(response));
    return await response.json();
  } finally { clearCsrfToken(); } // Next mutation acquires a token for the new identity.
}
export async function logout() {
  try {
    const response = await apiFetch(API_BASE_URL + '/api/auth/logout', { method: 'POST' });
    if (!response.ok) throw new Error(await readError(response));
  } finally { clearCsrfToken(); }
}
