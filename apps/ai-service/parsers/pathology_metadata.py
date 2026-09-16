"""Explicit, local report metadata. Never infer identity or patient demographics."""
import re
import math
from datetime import datetime

LABELS = {
    "laboratoryName": r"laboratory name|lab name|laboratory",
    "patientName": r"patient name|pt\.?\s*name|patient|^\s*name",
    "patientId": r"patient (?:id|no\.?)|uhid|mrn|registration no\.?",
    "ageSex": r"age\s*/\s*(?:gender|sex)",
    "age": r"patient age|age",
    "sex": r"gender\s*/\s*sex|sex|gender",
    "dateOfBirth": r"date of birth|d\.?o\.?b\.?",
    "bookingCode": r"booking code|booking no\.?",
    "accessionNumber": r"accession(?: number| no\.?)?",
    "reportNumber": r"report (?:number|no\.?)|lab no\.?",
    "bookingDate": r"booking date",
    "reportDate": r"report generated date|report(?:ed)? date|reported on|generated date|result date|date of report",
    "collectionDate": r"collection date|collected on|sample collected",
    "collectionTime": r"collection time",
    "referringDoctor": r"referring doctor|referred by|referral|ref\.? by|consultant",
    "specimenType": r"specimen type|specimen|sample type",
}
LABEL_PATTERN = re.compile(r"(?<!\w)(" + "|".join(f"(?P<{field}>{pattern})" for field, pattern in LABELS.items()) + r")\s*[:：]\s*", re.I)
EMPTY = {"", "-", "--", "n/a", "not stated", "not available"}


def explicit_date_iso(value):
    if not value:
        return None
    # Numeric day/month ordering is not inferred from locale.
    for fmt in ("%d %b %Y", "%d-%b-%Y", "%d %B %Y", "%d-%B-%Y", "%Y-%m-%d", "%Y/%m/%d"):
        try:
            return datetime.strptime(value.strip(), fmt).date().isoformat()
        except ValueError:
            pass
    numeric = re.fullmatch(r'(\d{1,2})[/-](\d{1,2})[/-](\d{4})', value.strip())
    if numeric:
        a, b, year = map(int, numeric.groups())
        # Only resolve day/month order when it is unambiguous.
        if a > 12 or b > 12 or a == b:
            day, month = (a, b) if a > 12 or a == b else (b, a)
            try: return datetime(year, month, day).date().isoformat()
            except ValueError: pass
    return None


AGE = r'(?P<age>\d+(?:\.\d+)?)\s*(?P<unit>years?|yrs?|y|months?|mos?|m|days?|d)?'


def normalized_age(value):
    match = re.fullmatch(AGE, value or '', re.I)
    if not match: return None, None
    number = float(match['age'])
    if not math.isfinite(number): return None, None
    unit = (match['unit'] or '').lower()
    return int(number) if number.is_integer() else number, ('YEARS' if unit.startswith('y') else 'MONTHS' if unit.startswith('m') else 'DAYS' if unit.startswith('d') else None)


def normalized_time(value):
    for fmt in ('%H:%M', '%H:%M:%S', '%I:%M %p', '%I:%M%p'):
        try: return datetime.strptime((value or '').strip(), fmt).strftime('%H:%M')
        except ValueError: pass
    return None


def date_and_time(value):
    if not value: return None, None
    parts = re.fullmatch(r'(.+?)(?:[T\s]+)(\d{1,2}:\d{2}(?::\d{2})?(?:\s*[AP]M)?)', value.strip(), re.I)
    return (explicit_date_iso(parts[1]), normalized_time(parts[2])) if parts else (explicit_date_iso(value), None)


def parse_pathology_metadata(pages, sections=(), page_count=None):
    candidates = {field: [] for field in LABELS}
    laboratory_headings = []
    issues = []
    for page in pages:
        # OCR demographics must not select ranges when token confidence is low.
        if page.source == "OCR" and (not page.tokens or min(t.confidence for t in page.tokens) < .8):
            issues.append({"code": "METADATA_LOW_CONFIDENCE", "page": page.page_number})
            continue
        for line in page.text.splitlines():
            matches = list(LABEL_PATTERN.finditer(line))
            # A name may end with "Patient" directly before an inline Age label.
            # The longer "Patient Age" alias is only distinct inline after a
            # column separator; otherwise retain that word in the patient's name.
            for i, match in enumerate(matches):
                if i and match.group('age') and match.group('age').lower() == 'patient age' and matches[i-1].group('patientName') and not re.search(r'(?:\s{2,}|\t)$', line[:match.start()]):
                    matches[i] = LABEL_PATTERN.search(line, match.start() + len('patient '))
            for index, match in enumerate(matches):
                field = next(name for name in LABELS if match.group(name) is not None)
                value = line[match.end():matches[index + 1].start() if index + 1 < len(matches) else len(line)].strip()
                if value.casefold() not in EMPTY:
                    candidates[field].append(value)
            heading = line.strip()
            if not matches and re.fullmatch(r"[A-Za-z][A-Za-z &().'\-]{1,100}\b(?:LABORATORY|LABORATORIES|DIAGNOSTICS?|PATHOLOGY LAB)", heading, re.I):
                laboratory_headings.append(heading)
    # A split logo can print only the suffix on one line, with the complete
    # laboratory name printed elsewhere. Use that observed complete heading;
    # do not concatenate unrelated lines or resolve conflicting full names.
    candidates['laboratoryName'].extend(
        heading for heading in laboratory_headings
        if not any(other.casefold().endswith(' ' + heading.casefold()) for other in laboratory_headings)
    )
    for value in candidates['ageSex']:
        match = re.fullmatch(rf'({AGE})\s*/\s*(male|female|m|f)', value, re.I)
        if match:
            candidates['age'].append(match[1])
            candidates['sex'].append(match[4])
        else:
            # Explicit ambiguous combined metadata must not be overridden by another label.
            candidates['age'].append('ambiguous')
            candidates['sex'].append('ambiguous')
            issues.append({'code': 'METADATA_AMBIGUOUS', 'field': 'ageSex'})
    data = {}
    for field, values in candidates.items():
        def key(value):
            if field == 'age': return str(normalized_age(value)) if normalized_age(value)[0] is not None else value.casefold()
            if field == 'sex': return {'m': 'male', 'f': 'female'}.get(value.casefold(), value.casefold())
            return " ".join(value.casefold().split())
        unique = {key(value): value for value in values}
        data[field] = next(iter(unique.values())) if len(unique) == 1 else None
        if len(unique) > 1:
            issues.append({"code": "METADATA_CONFLICT", "field": field})
    age, age_unit = normalized_age(data['age'])
    if data['age'] and age is None:
        data['age'] = None
        issues.append({"code": "METADATA_AMBIGUOUS", "field": "age"})
    if data['sex']:
        sex = data['sex'].casefold()
        data['sex'] = {"male": "Male", "m": "Male", "female": "Female", "f": "Female"}.get(sex)
        if data['sex'] is None:
            issues.append({"code": "METADATA_AMBIGUOUS", "field": "sex"})
    data['reportDateIso'] = explicit_date_iso(data['reportDate'])
    report_date, _ = date_and_time(data['reportDate'])
    data['reportDateIso'] = report_date
    collection_date, collection_time = date_and_time(data['collectionDate'])
    explicit_time = normalized_time(data['collectionTime'])
    if explicit_time and collection_time and explicit_time != collection_time:
        collection_time = None
        issues.append({'code': 'METADATA_CONFLICT', 'field': 'collectionTime'})
    elif data['collectionTime']:
        collection_time = explicit_time
    for field, normalized in [('dateOfBirth', explicit_date_iso(data['dateOfBirth'])), ('reportDate', report_date), ('collectionDate', collection_date), ('bookingDate', explicit_date_iso(data['bookingDate'])), ('collectionTime', collection_time)]:
        if data[field] and normalized is None:
            issues.append({'code': 'METADATA_AMBIGUOUS', 'field': field})
    data['sections'] = list(dict.fromkeys(section for section in sections if section))
    data['panels'] = list(data['sections'])
    data['pageCount'] = page_count if page_count is not None else len(pages)
    data['metadataIssues'] = issues
    # Canonical, typed header contract. Flat keys remain for existing clients;
    # raw printed values remain available without replacing them with guesses.
    data['patient'] = {'name': data['patientName'], 'patientId': data['patientId'], 'age': age, 'ageUnit': age_unit,
                       'sex': data['sex'].upper() if data['sex'] else None, 'dateOfBirth': explicit_date_iso(data['dateOfBirth'])}
    data['report'] = {key: data[key] for key in ('laboratoryName', 'reportNumber', 'bookingCode', 'accessionNumber', 'referringDoctor', 'specimenType', 'pageCount')}
    data['report'].update(reportGeneratedDate=report_date, bookingDate=explicit_date_iso(data['bookingDate']), collectionDate=collection_date, collectionTime=collection_time)
    data['rawFields'] = {field: list(dict.fromkeys(values)) for field, values in candidates.items() if values}
    return data
