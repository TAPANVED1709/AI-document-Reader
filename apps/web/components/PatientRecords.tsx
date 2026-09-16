'use client';
import { useEffect, useState } from 'react';
import { getPatientData, getProcessingStatus, logout, uploadReport } from '../lib/api';
import type { CurrentUser, Report } from '../lib/types';
import { dateLabel, trendSeries, type Measurement, type MedicalRecord, type Page } from '../lib/patient-records';
import { displayValue } from '../lib/formatters';
import { groupPathologyResults } from '../lib/panels';
import UploadZone from './UploadZone';
import PdfViewer from './PdfViewer';
import ReportHeader from './ReportHeader';
import ResultStatus from './ResultStatus';
import SafetyNotice from './SafetyNotice';

const button = 'focus-ring rounded-lg border border-line bg-white px-3 py-2 text-sm font-semibold';
const panel = 'rounded-xl border border-line bg-white p-5 shadow-sm';
type View = 'Dashboard' | 'Upload Report' | 'My Medical Records' | 'Health Timeline' | 'Test Trends';

function usePatientData<T>(path: string) {
  const [state, setState] = useState<{ path: string; data?: T; error?: string }>({ path: '' });
  useEffect(() => {
    let active = true;
    getPatientData<T>(path).then(data => { if (active) setState({ path, data }); })
      .catch(error => { if (active) setState({ path, error: error instanceof Error ? error.message : 'Unable to load your records.' }); });
    return () => { active = false; };
  }, [path]);
  return state.path === path ? state : { path };
}
function State({ error }: { error?: string }) { return error ? <p role="alert" className={panel}>{error} Use navigation to retry.</p> : <p role="status" className={panel}>Loading your medical records…</p>; }
function Pagination({ total, page, setPage }: { total: number; page: number; setPage: (page: number) => void }) {
  return <nav aria-label="Record pages" className="mt-4 flex items-center gap-3"><button className={button} disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button><span className="text-sm">Page {page} of {Math.max(1, Math.ceil(total / 20))}</span><button className={button} disabled={page * 20 >= total} onClick={() => setPage(page + 1)}>Next</button></nav>;
}

export default function PatientRecords({ user, onLogout }: { user: CurrentUser; onLogout: () => void }) {
  const [view, setView] = useState<View>('Dashboard'); const [reportId, setReportId] = useState<string | null>(null);
  const [error, setError] = useState(''); const [revision, setRevision] = useState(0);
  const open = (id: string) => { setReportId(id); setError(''); };
  const navigate = (next: View) => { setReportId(null); setView(next); setError(''); setRevision(r => r + 1); };
  const upload = async (file: File) => {
    try {
      setError(''); const queued = await uploadReport(file, true);
      if (!('jobId' in queued)) { open(queued.id); return; }
      for (let attempt = 0; attempt < 150; attempt++) {
        const status = await getProcessingStatus(queued.reportId, true);
        if (status.status === 'FAILED') throw new Error('Processing could not complete. Your report remains in My Medical Records.');
        if (['COMPLETED', 'REVIEW_REQUIRED'].includes(status.status)) { open(queued.reportId); return; }
        await new Promise(resolve => setTimeout(resolve, 2000));
      }
      throw new Error('Still processing. You can find this upload in My Medical Records.');
    } catch (e) { const message = e instanceof Error ? e.message : 'Upload unavailable.'; setError(message); throw e; }
  };
  return <main className="min-h-screen"><header className="border-b border-line bg-white"><div className="mx-auto flex max-w-7xl flex-wrap items-center justify-between gap-3 px-6 py-5"><div><p className="text-xs font-bold uppercase tracking-wider text-teal">Private patient workspace</p><h1 className="text-2xl font-bold">My Medical Records</h1></div><div className="text-sm"><span>{user.firstName || 'Patient'}</span><button className={`${button} ml-3`} onClick={async () => { try { await logout(); onLogout(); } catch { setError('Unable to log out. Please try again.'); } }}>Log out</button></div></div></header>
    <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6"><nav aria-label="Patient navigation" className="mb-6 flex flex-wrap gap-2">{(['Dashboard', 'Upload Report', 'My Medical Records', 'Health Timeline', 'Test Trends'] as View[]).map(item => <button key={item} aria-current={!reportId && view === item ? 'page' : undefined} className={`${button} ${!reportId && view === item ? '!bg-ink text-white' : ''}`} onClick={() => navigate(item)}>{item}</button>)}</nav>
      {error && <p role="alert" className="mb-4 rounded bg-red-50 p-3 text-red-800">{error}</p>}
      {reportId ? <PatientReport key={reportId} id={reportId} back={() => navigate('My Medical Records')} /> : <div key={`${view}-${revision}`}>
        <h2 className="mb-4 text-2xl font-bold">{view}</h2>
        {view === 'Upload Report' ? <UploadZone onUpload={upload} /> : view === 'Test Trends' ? <PatientTrends open={open} /> : view === 'Dashboard' ? <><p className="mb-4 text-slate-600">Your reports and stored measurements, with source dates and review status preserved.</p><button className={`${button} mb-5 !bg-teal text-white`} onClick={() => navigate('Upload Report')}>Upload a report</button><Latest open={open} /><Records open={open} upload={() => navigate('Upload Report')} /></> : <Records timeline={view === 'Health Timeline'} open={open} upload={() => navigate('Upload Report')} />}
      </div>}<div className="mt-8"><SafetyNotice /></div>
    </div></main>;
}

function Records({ timeline = false, open, upload }: { timeline?: boolean; open: (id: string) => void; upload: () => void }) {
  const [page, setPage] = useState(1); const [filter, setFilter] = useState('ALL'); const [search, setSearch] = useState(''); const [term, setTerm] = useState('');
  const path = `${timeline ? 'medical-timeline' : 'medical-records'}?${new URLSearchParams({ page: String(page), pageSize: '20', filter, search: term })}`;
  const { data, error } = usePatientData<Page<MedicalRecord>>(path);
  return <section aria-label={timeline ? 'Medical timeline' : 'Medical report list'}>
    {!timeline && <form onSubmit={e => { e.preventDefault(); setTerm(search); setPage(1); }} className="mb-4 flex flex-wrap gap-2"><label className="flex-1 text-sm">Search laboratory, filename or report number<input className="focus-ring mt-1 block w-full rounded-lg border border-line p-2" value={search} onChange={e => setSearch(e.target.value)} /></label><button className={button}>Search</button><label className="text-sm">Status<select aria-label="Record status" className="focus-ring mt-1 block rounded-lg border border-line p-2" value={filter} onChange={e => { setFilter(e.target.value); setPage(1); }}><option value="ALL">All</option><option value="COMPLETED">Completed</option><option value="NEEDS_REVIEW">Needs Review</option></select></label></form>}
    {!data ? <State error={error} /> : <>{data.items.length ? <div className="grid gap-3 md:grid-cols-2">{data.items.map(record => <article key={record.reportId} className={panel}>
      <p className="text-sm font-semibold text-teal">{dateLabel(record.reportGeneratedDate, record.uploadedAt)}</p><h3 className="mt-2 font-bold">{record.laboratoryName || 'Laboratory not provided'}</h3><p className="break-words text-sm">{record.originalFileName}</p>{record.reportNumber && <p className="text-xs text-slate-500">Report {record.reportNumber}</p>}
      <p className="mt-2 text-sm">{record.testCount} tests · {record.normalCount} Normal · {record.highCount} High · {record.lowCount} Low · {record.needsReviewCount} Needs Review</p><p className="mt-1 text-xs text-slate-600">{record.status.replaceAll('_', ' ')} · {record.processingMode}</p><button className={`${button} mt-3`} onClick={() => open(record.reportId)}>View Report</button>
    </article>)}</div> : <div className={panel}><p>{filter !== 'ALL' || term ? 'No reports match these filters.' : 'No medical reports uploaded yet.'}</p><button className={`${button} mt-3`} onClick={upload}>Upload your first report</button></div>}<Pagination total={data.total} page={page} setPage={setPage} /></>}
  </section>;
}

function PatientReport({ id, back }: { id: string; back: () => void }) {
  const { data: report, error } = usePatientData<Report & { status: string }>(`medical-records/${id}`); const [page, setPage] = useState(1);
  if (!report) return <State error={error} />;
  return <><button className={`${button} mb-4`} onClick={back}>Back to My Medical Records</button><h2 className="mb-2 break-words text-xl font-bold">{report.originalFileName}</h2><p className="mb-4 text-sm">{report.status.replaceAll('_', ' ')} · {report.resultsCount} results · {report.processingMode}</p><ReportHeader report={report} />
    <div className="grid gap-5 md:grid-cols-2"><PdfViewer reportId={id} page={page} setPage={setPage} /><section aria-label="Extracted results"><h2 className="mb-3 text-xl font-bold">Extracted results</h2>{!report.results.length && <p className={panel}>No extracted results available. Processing or review may still be needed.</p>}{groupPathologyResults(report.results).map(group => <section key={group.panel} className="mb-5"><h3 className="mb-2 font-bold">{group.panel}</h3>{group.results.map(result => <article key={result.id} className={`${panel} mb-3`}>
      <div className="flex flex-wrap items-start justify-between gap-3"><div><h4 className="font-bold">{result.normalizedTestName || result.originalTestName}</h4><p className="text-xs text-slate-500">Original: {result.originalTestName}</p><p className="my-2 text-xl font-bold">{displayValue(result.valueNumeric, result.valueText)} {result.unit}</p><p className="text-sm">Reference: {result.referenceText || 'Not provided'}</p>{result.methodText && <p className="text-xs">Method: {result.methodText}</p>}</div><ResultStatus result={result} /></div>
      {(result.reviewRequired || result.calculatedStatus === 'UNKNOWN') && <p className="mt-3 rounded bg-amber-50 p-2 text-sm text-amber-900">Needs review: {result.validationIssues?.find(i => i.requiresReview && !i.isResolved)?.message || 'Please ask your laboratory to check this extracted value against the original report.'}</p>}
      <button className={`${button} mt-3`} onClick={() => setPage(result.pageNumber)}>View source page {result.pageNumber}</button>
    </article>)}</section>)}</section></div></>;
}

function Latest({ open }: { open: (id: string) => void }) {
  const [page, setPage] = useState(1); const { data, error } = usePatientData<Page<Measurement>>(`latest-results?page=${page}&pageSize=20`);
  return <section className="mb-7" aria-label="Latest trusted results"><h3 className="mb-3 text-lg font-bold">Latest Trusted Results</h3>{!data ? <State error={error} /> : <>{data.items.length ? <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">{data.items.map(p => <article key={p.id} className={panel}><h4 className="font-bold">{p.testName}</h4><p>{p.value} {p.unit} · {p.status}</p><p className="text-xs">{dateLabel(p.reportDate, p.uploadedAt)}</p><button className={`${button} mt-2`} onClick={() => open(p.reportId)}>View source report</button></article>)}</div> : <p className={panel}>No trusted numeric results available yet.</p>}<Pagination total={data.total} page={page} setPage={setPage} /></>}</section>;
}

function PatientTrends({ open }: { open: (id: string) => void }) {
  const [input, setInput] = useState('Creatinine'); const [name, setName] = useState('Creatinine'); const [page, setPage] = useState(1);
  const { data, error } = usePatientData<Page<Measurement>>(`test-history/${encodeURIComponent(name)}?page=${page}&pageSize=20`);
  return <section><form className="mb-4 flex flex-wrap gap-2" onSubmit={e => { e.preventDefault(); if (input.trim()) { setName(input.trim()); setPage(1); } }}><label className="text-sm">Normalized test name<input required value={input} onChange={e => setInput(e.target.value)} className="focus-ring ml-2 rounded border border-line p-2" /></label><button className={button}>Show history</button></form><p className="mb-4 text-sm text-slate-600">Actual stored measurements only. Uncertain results are excluded. Each point retains its own printed range. No forecast or universal normal band.</p>{!data ? <State error={error} /> : <>{data.items.length ? <><PatientTrendChart points={data.items} name={name} open={open} /><div className="mt-4 overflow-x-auto"><table className="w-full bg-white text-left text-sm"><caption className="p-2 text-left">Trusted measurements · page {page} · {data.total} total</caption><thead><tr>{['Date', 'Laboratory / original test', 'Value', 'Printed range', 'Status', 'Source'].map(h => <th key={h} className="p-2">{h}</th>)}</tr></thead><tbody>{data.items.map(p => <tr key={p.id} className="border-t border-line"><td className="p-2">{dateLabel(p.reportDate, p.uploadedAt)}</td><td className="p-2">{p.laboratoryName || 'Not provided'}<br />{p.originalTestName}</td><td className="p-2">{p.value} {p.unit}</td><td className="p-2">{p.referenceRange || 'Not provided'}</td><td className="p-2">{p.status}</td><td className="p-2"><button className={button} onClick={() => open(p.reportId)}>View source report</button></td></tr>)}</tbody></table></div></> : <p className={panel}>No trusted numeric history found for this test.</p>}<p className="mt-3 text-sm">{data.excludedCount || 0} result(s) excluded pending review or exact numeric representation.</p><Pagination total={data.total} page={page} setPage={setPage} /></>}</section>;
}

export function PatientTrendChart({ points, name, open }: { points: Measurement[]; name: string; open: (id: string) => void }) {
  const [selected, setSelected] = useState<Measurement | null>(null); const groups = trendSeries(points);
  return <div>{groups.length > 1 && <p className="mb-3 text-sm">Unit changed — comparison across units unavailable. Separate series are shown.</p>}{points.some(p => !p.reportDate || !p.unit) && <p className="mb-3 text-sm">Measurements without a report date or unit remain in the table and are not plotted.</p>}{groups.map(group => {
    const values = group.points.map(p => p.value); const low = Math.min(...values); const high = Math.max(...values); const span = high - low || 1;
    const dates = group.points.map(p => Date.parse(p.reportDate!)); const start = Math.min(...dates); const duration = Math.max(...dates) - start || 1;
    const x = (i: number) => 55 + (dates[i] - start) / duration * 400; const y = (p: Measurement) => 140 - (p.value - low) / span * 100;
    return <div key={group.unit} className={`${panel} mb-3`}><h3 className="font-bold">{name} · {group.unit}</h3><svg viewBox="0 0 520 185" role="group" aria-label={`${name} measurements in ${group.unit}`} className="w-full"><text x="2" y="40" fontSize="11">{high}</text><text x="2" y="140" fontSize="11">{low}</text><line x1="50" y1="145" x2="480" y2="145" stroke="#94a3b8" />{group.points.length > 1 && <polyline data-testid="unit-series" points={group.points.map((p, i) => `${x(i)},${y(p)}`).join(' ')} fill="none" stroke="#147d83" strokeWidth="2" />}{group.points.map((p, i) => <circle key={p.id} cx={x(i)} cy={y(p)} r="6" fill="#147d83" role="button" tabIndex={0} aria-label={`${p.reportDate}: ${p.value} ${p.unit}, ${p.originalTestName}`} onClick={() => setSelected(p)} onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setSelected(p); } }}><title>{p.reportDate}: {p.value} {p.unit}; reference {p.referenceRange}</title></circle>)}<text x="55" y="170" fontSize="11">{group.points[0].reportDate}</text><text x="455" y="170" fontSize="11" textAnchor="end">{group.points.at(-1)!.reportDate}</text></svg></div>;
  })}{selected && <aside aria-label="Selected measurement" className={panel}><h3 className="font-bold">{dateLabel(selected.reportDate, selected.uploadedAt)}</h3><p>{selected.laboratoryName || 'Laboratory not provided'} · {selected.originalTestName}</p><p>{selected.value} {selected.unit} · {selected.status}</p><p>Printed reference: {selected.referenceRange || 'Not provided'}</p><button className={`${button} mt-2`} onClick={() => open(selected.reportId)}>View source report</button></aside>}</div>;
}
