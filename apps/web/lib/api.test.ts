import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const rejected = () => response({ error: 'A valid CSRF token is required.' }, 400);
const file = () => new File(['%PDF-1.4'], 'synthetic.pdf', { type: 'application/pdf' });
let fetchMock: ReturnType<typeof vi.fn>;
beforeEach(() => { vi.resetModules(); fetchMock = vi.fn(); vi.stubGlobal('fetch', fetchMock); });
afterEach(() => vi.unstubAllGlobals());

describe('existing antiforgery token lifecycle', () => {
  it('uses anonymous issuance for login then reacquires for authenticated multipart upload', async () => {
    fetchMock.mockResolvedValueOnce(response({ token: 'anonymous' })).mockResolvedValueOnce(response({ role: 'LAB_STAFF' }))
      .mockResolvedValueOnce(response({ token: 'authenticated' })).mockResolvedValueOnce(response({ jobId: 'job', status: 'QUEUED' }, 202));
    const api = await import('./api');
    await api.login('staff@test', 'test-password');
    await expect(api.uploadReport(file())).resolves.toMatchObject({ status: 'QUEUED' });
    expect(fetchMock.mock.calls.map(c => String(c[0]).split('/api/')[1])).toEqual(['auth/csrf', 'auth/login', 'auth/csrf', 'reports/upload']);
    expect(fetchMock.mock.calls[1][1].headers.get('X-XSRF-TOKEN')).toBe('anonymous');
    const upload = fetchMock.mock.calls[3][1];
    expect(upload.headers.get('X-XSRF-TOKEN')).toBe('authenticated');
    expect(upload.body).toBeInstanceOf(FormData);
    expect(upload.body.get('file').name).toBe('synthetic.pdf');
    expect(upload.headers.has('Content-Type')).toBe(false);
    for (const call of fetchMock.mock.calls) expect(call[1].credentials).toBe('include');
  });

  it('clears the authenticated token after logout before the next login', async () => {
    fetchMock.mockResolvedValueOnce(response({ token: 'authenticated' })).mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(response({ token: 'anonymous' })).mockResolvedValueOnce(response({ role: 'LAB_STAFF' }));
    const api = await import('./api');
    await api.logout(); await api.login('staff@test', 'test-password');
    expect(fetchMock.mock.calls[3][1].headers.get('X-XSRF-TOKEN')).toBe('anonymous');
  });

  it('refreshes a stale token once and replays the same multipart body', async () => {
    fetchMock.mockResolvedValueOnce(response({ token: 'stale' })).mockResolvedValueOnce(rejected())
      .mockResolvedValueOnce(response({ token: 'fresh' })).mockResolvedValueOnce(response({ status: 'QUEUED' }, 202));
    const api = await import('./api');
    await expect(api.uploadReport(file())).resolves.toMatchObject({ status: 'QUEUED' });
    expect(fetchMock).toHaveBeenCalledTimes(4);
    expect(fetchMock.mock.calls[3][1].headers.get('X-XSRF-TOKEN')).toBe('fresh');
    expect(fetchMock.mock.calls[3][1].body).toBe(fetchMock.mock.calls[1][1].body);
  });

  it('stops after one retry when the fresh token is also rejected', async () => {
    fetchMock.mockResolvedValueOnce(response({ token: 'stale' })).mockResolvedValueOnce(rejected())
      .mockResolvedValueOnce(response({ token: 'fresh' })).mockResolvedValueOnce(rejected());
    const api = await import('./api');
    await expect(api.uploadReport(file())).rejects.toThrow('A valid CSRF token is required.');
    expect(fetchMock).toHaveBeenCalledTimes(4);
  });

  it.each([400, 401, 403, 500])('does not retry an unrelated HTTP %s failure', async status => {
    fetchMock.mockResolvedValueOnce(response({ token: 'valid' })).mockResolvedValueOnce(response({ error: 'Other failure' }, status));
    const api = await import('./api');
    await expect(api.uploadReport(file())).rejects.toThrow('Other failure');
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('does not send a mutation if token issuance fails', async () => {
    fetchMock.mockResolvedValueOnce(response({}, 503));
    const api = await import('./api');
    await expect(api.uploadReport(file())).rejects.toThrow('Could not obtain a CSRF token');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('shares token issuance between simultaneous mutations', async () => {
    fetchMock.mockImplementation(async (url: string) => url.endsWith('/csrf') ? response({ token: 'shared' }) : response({ status: 'QUEUED' }, 202));
    const api = await import('./api');
    await Promise.all([api.uploadReport(file()), api.uploadReport(file())]);
    expect(fetchMock.mock.calls.filter(c => String(c[0]).endsWith('/csrf'))).toHaveLength(1);
  });

  it('reports logout failure instead of pretending logout succeeded', async () => {
    fetchMock.mockResolvedValueOnce(response({ token: 'old' })).mockResolvedValueOnce(response({ error: 'Unavailable' }, 503))
      .mockResolvedValueOnce(response({ token: 'new' })).mockResolvedValueOnce(response({ status: 'QUEUED' }, 202));
    const api = await import('./api');
    await expect(api.logout()).rejects.toThrow('Unavailable');
    await api.uploadReport(file());
    expect(fetchMock.mock.calls[3][1].headers.get('X-XSRF-TOKEN')).toBe('new');
  });
});
