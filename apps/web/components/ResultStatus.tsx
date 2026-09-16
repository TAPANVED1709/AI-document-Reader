import type { LabResult } from '../lib/types';
import { statusIcon } from '../lib/formatters';

const reasons: Record<string, string> = {
  PATIENT_SEX_MISSING: 'Patient sex not available',
  PATIENT_SEX_MISMATCH: 'Printed range is for a different patient sex',
  PATIENT_AGE_MISSING: 'Patient age not available',
  PATIENT_AGE_MISMATCH: 'Patient age is outside the printed age range',
  AGE_CRITERIA_UNSPECIFIED: 'Printed age criteria are not specified',
  AGE_RANGE_AMBIGUOUS: 'Printed age range is ambiguous',
  PREGNANCY_METADATA_MISSING: 'Pregnancy metadata is not available',
  DEMOGRAPHIC_QUALIFIER_AMBIGUOUS: 'Printed demographic qualifier is ambiguous',
};

export default function ResultStatus({ result }: { result: LabResult }) {
  const unresolved = result.calculatedStatus === 'UNKNOWN';
  const demographicReview = result.applicabilityStatus === 'REVIEW_REQUIRED';
  const notApplicable = result.applicabilityStatus === 'NOT_APPLICABLE';
  return <div className="flex flex-col items-end gap-2 text-right text-xs">
    <span className="rounded-full bg-slate-100 px-2.5 py-1 font-bold">{unresolved ? 'NEEDS REVIEW' : `${statusIcon[result.calculatedStatus]} ${result.calculatedStatus}`}</span>
    {(demographicReview || notApplicable) && <>
      <span className="rounded-lg bg-amber-50 px-2.5 py-1 font-bold text-amber-800">{notApplicable ? 'Not applicable to current patient demographics' : 'Demographic review required'}</span>
      {result.applicabilityReason && <span className="text-amber-900">Reason: {reasons[result.applicabilityReason] || 'Verify printed demographic applicability'}</span>}
    </>}
    {!demographicReview && !notApplicable && !unresolved && (result.reviewRequired || result.validationIssues?.some(issue => issue.requiresReview && !issue.isResolved)) && <span className="rounded-full bg-amber-50 px-2.5 py-1 font-bold text-amber-800">Review Required</span>}
  </div>;
}
