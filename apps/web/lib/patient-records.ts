export type Page<T> = { items: T[]; total: number; page: number; pageSize: number; excludedCount?: number };
export type MedicalRecord = {
  reportId: string; originalFileName: string; laboratoryName: string | null; reportNumber: string | null;
  reportGeneratedDate: string | null; uploadedAt: string; documentType: string; processingMode: string;
  status: string; reviewRequired: boolean; testCount: number; highCount: number; lowCount: number; normalCount: number; needsReviewCount: number;
};
export type Measurement = {
  id: string; reportId: string; testName: string; originalTestName: string; reportDate: string | null; uploadedAt: string;
  laboratoryName: string | null; value: number; unit: string | null; referenceRange: string | null; status: string;
};
export const dateLabel = (date: string | null | undefined, uploadedAt: string) => date
  ? new Date(date + 'T00:00:00').toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })
  : `Uploaded ${new Date(uploadedAt).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })}`;

// Keep exact persisted units in separate series. Missing units do not form a numeric line.
export function trendSeries(points: Measurement[]) {
  const series = new Map<string, Measurement[]>();
  for (const point of points) {
    if (!point.reportDate || !point.unit || !Number.isFinite(point.value)) continue;
    const group = series.get(point.unit) || []; group.push(point); series.set(point.unit, group);
  }
  return Array.from(series, ([unit, items]) => ({ unit, points: items.sort((a, b) => a.reportDate!.localeCompare(b.reportDate!)) }));
}
