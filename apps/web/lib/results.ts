import type { Filter, LabResult } from './types';
import { confidenceNeedsReview } from './confidence';

export function filterResults(results: LabResult[], filter: Filter, query: string) {
  const filtered = results.filter(result => {
    const name = (result.normalizedTestName || result.originalTestName).toLowerCase();
    const matchesName = name.includes(query.toLowerCase());
    const matchesFilter = filter === 'All' || filter === result.calculatedStatus ||
      (filter === 'Needs Review' && (result.reviewRequired || confidenceNeedsReview(result.extractionConfidence))) ||
      (filter === 'Verified' && result.isVerified);
    return matchesName && matchesFilter;
  });
  if (filter !== 'Needs Review') return filtered;
  const rank = { HIGH: 0, WARNING: 1, INFO: 2 } as const;
  return filtered.slice().sort((a, b) =>
    (rank[a.validationIssues?.find(i => i.requiresReview)?.severity || 'INFO'] ?? 2) -
    (rank[b.validationIssues?.find(i => i.requiresReview)?.severity || 'INFO'] ?? 2)
  );
}
