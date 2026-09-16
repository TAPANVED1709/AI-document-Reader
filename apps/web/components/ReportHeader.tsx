import type { Report } from '../lib/types';

function object(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {};
}
function text(value: unknown): string {
  return typeof value === 'number' || typeof value === 'string' && value.trim() ? String(value) : 'Not provided';
}

export default function ReportHeader({ report }: { report: Report }) {
  const data = report.structuredData || {};
  const patient = object(data.patient); const header = object(data.report);
  // Legacy records retain their explicitly extracted raw metadata. Never use
  // uploadedAt or the timeline's fallback date as a generated report date.
  const p = (key: string, legacy?: string) => 'patient' in data ? patient[key] : legacy ? data[legacy] : undefined;
  const r = (key: string, legacy = key) => 'report' in data ? header[key] : data[legacy];
  const age = p('age', 'age'); const unit = p('ageUnit');
  const patientFields = [['Name', p('name', 'patientName')], ['Age', age == null ? null : `${age}${unit ? ` ${String(unit).toLowerCase()}` : ''}`], ['Gender', p('sex', 'sex')], ['DOB', p('dateOfBirth', 'dateOfBirth')], ['Patient ID', p('patientId', 'patientId')]];
  const reportFields = [['Laboratory', r('laboratoryName')], ['Report Generated Date', r('reportGeneratedDate', 'reportDateIso')], ['Report Number', r('reportNumber')], ['Booking Code', r('bookingCode')], ['Accession Number', r('accessionNumber')], ['Booking Date', r('bookingDate')], ['Collection Date', r('collectionDate')], ['Collection Time', r('collectionTime')], ['Referring Doctor', r('referringDoctor')], ['Specimen', r('specimenType')], ['Page Count', r('pageCount')]];
  return <section aria-label="Report header" className="mb-5 grid gap-6 rounded-xl border border-line bg-white p-5 shadow-sm md:grid-cols-2">
    {([['Patient', patientFields], ['Report', reportFields]] as const).map(([title, fields]) => <div key={title}>
      <h3 className="mb-3 text-xs font-bold uppercase tracking-wider text-teal">{title}</h3>
      <dl className="grid grid-cols-2 gap-x-5 gap-y-3">{fields.map(([label, value]) => <div key={String(label)} className={label === 'Laboratory' || label === 'Name' ? 'col-span-2' : ''}>
        <dt className="text-xs text-slate-500">{String(label)}</dt><dd className="mt-1 break-words text-sm font-semibold text-ink">{text(value)}</dd>
      </div>)}</dl>
    </div>)}
  </section>;
}
