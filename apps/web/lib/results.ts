import type { Filter, LabResult } from './types';
import { confidenceNeedsReview } from './confidence';

export function filterResults(results: LabResult[], filter: Filter, query: string) {
  return results.filter(result => {
    const name = (result.normalizedTestName || result.originalTestName).toLowerCase();
    const matchesName = name.includes(query.toLowerCase());
    const matchesFilter = filter === 'All' || filter === result.calculatedStatus ||
      (filter === 'Needs Review' && confidenceNeedsReview(result.extractionConfidence)) ||
      (filter === 'Verified' && result.isVerified);
    return matchesName && matchesFilter;
  });
}
