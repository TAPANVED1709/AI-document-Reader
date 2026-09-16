"""Printed row qualifiers and patient applicability, independent of arithmetic."""
import re

SUFFIX = re.compile(r"\s*(?:\((?P<label>male|female|adult|pediatric|paediatric|pregnant|pregnancy|\d+(?:\.\d+)?\s*[-–]\s*\d+(?:\.\d+)?\s*(?:years?|months?))\)|\b(?P<bare>male|female)\s*:?)\s*$", re.I)


def row_applicability(name, age_years, sex):
    match = SUFFIX.search(name)
    if not match:
        return name, None, 'NOT_REQUIRED', None
    label = (match['label'] or match['bare']).lower()
    identity = name[:match.start()].strip()
    if SUFFIX.search(identity):
        return identity, None, 'REVIEW_REQUIRED', 'DEMOGRAPHIC_QUALIFIER_AMBIGUOUS'
    if label in ('male', 'female'):
        if sex is None:
            return identity, label.upper(), 'REVIEW_REQUIRED', 'PATIENT_SEX_MISSING'
        applies = sex.lower()[0] == label[0]
        return identity, label.upper(), 'APPLICABLE' if applies else 'NOT_APPLICABLE', None if applies else 'PATIENT_SEX_MISMATCH'
    age_range = re.fullmatch(r'(\d+(?:\.\d+)?)\s*[-–]\s*(\d+(?:\.\d+)?)\s*(years?|months?)', label)
    if age_range:
        low, high = float(age_range[1]), float(age_range[2])
        if age_range[3].startswith('month'): low, high = low / 12, high / 12
        if low > high: return identity, 'AGE_RANGE', 'REVIEW_REQUIRED', 'AGE_RANGE_AMBIGUOUS'
        if age_years is None: return identity, 'AGE_RANGE', 'REVIEW_REQUIRED', 'PATIENT_AGE_MISSING'
        applies = low <= age_years <= high
        return identity, 'AGE_RANGE', 'APPLICABLE' if applies else 'NOT_APPLICABLE', None if applies else 'PATIENT_AGE_MISMATCH'
    qualifier = 'PREGNANCY' if label.startswith('pregnan') else 'ADULT' if label == 'adult' else 'PEDIATRIC'
    # A label alone does not print an age cutoff or establish pregnancy.
    return identity, qualifier, 'REVIEW_REQUIRED', 'PREGNANCY_METADATA_MISSING' if qualifier == 'PREGNANCY' else 'AGE_CRITERIA_UNSPECIFIED'
