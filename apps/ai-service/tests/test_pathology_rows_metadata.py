import pytest
from core.extractor import PageData, OcrToken
from parsers.lab_parser import LabRowParser
from parsers.pathology_metadata import parse_pathology_metadata
from validation import ValidationEngine


def page(text, number=1):
    return PageData(number, text, len(text))


def parse(text):
    rows = LabRowParser().parse([page(text)])
    for row in rows:
        row.sourceType = 'NATIVE_TEXT'
    ValidationEngine().validate(rows)
    return rows


@pytest.mark.parametrize('text,value,unit,low,high,status', [
    ('Total Protein 6.9 6-8.3 g/dL / 60-83 g/L', 6.9, 'g/dL', 6, 8.3, 'NORMAL'),
    ('Gamma GT (GGT) 10 9-48 U/L / 0.2-0.8 µkat/L', 10, 'U/L', 9, 48, 'NORMAL'),
    ('GGT 10 9-48 U/L / 0.2-0.8 µkat/L', 10, 'U/L', 9, 48, 'NORMAL'),
    ('Sodium (Na+) 167 135-145 mEq/L / 135-145 mmol/L', 167, 'mEq/L', 135, 145, 'HIGH'),
    ('Ionized Calcium 1.9 1.1-1.3 mmol/L / 1.1-1.3 mmol/L', 1.9, 'mmol/L', 1.1, 1.3, 'HIGH'),
    ('Blood Glucose - Fasting 92 70-100 mg/dl / 3.9-5.6 mmol/L', 92, 'mg/dL', 70, 100, 'NORMAL'),
    ('Total Protein 5 6-8.3 g/dL / 60-83 g/L', 5, 'g/dL', 6, 8.3, 'LOW'),
    ('Total Protein 69 g/L 6-8.3 g/dL / 60-83 g/L', 69, 'g/L', 60, 83, 'NORMAL'),
])
def test_primary_printed_range_and_unit_are_associated_without_conversion(text, value, unit, low, high, status):
    row, = parse(text)
    assert (row.value, row.normalizedUnit, row.referenceMin, row.referenceMax) == (value, unit, low, high)
    assert row.referenceType == 'BETWEEN'
    printed = text.split(f' {value:g} ', 1)[1]
    if printed.startswith(row.originalUnit + ' '):
        printed = printed[len(row.originalUnit):].strip()
    assert row.referenceText == printed
    assert ' / ' in row.referenceText
    assert row.calculatedStatus == status
    assert row.ambiguityReason is None and not row.reviewRequired
    assert row.fieldConfidences['ref'] >= .8


@pytest.mark.parametrize('text', [
    'Total Protein 6.9 mg/L 6-8.3 g/dL / 60-83 g/L',
    'Sodium 140 135-145 mmol/L / 120-130 mmol/L',
    'Total Protein 6.9 6-8.3 g/dL / unclear 60-83 g/L',
])
def test_unresolved_unit_or_reference_association_stays_unknown(text):
    row, = parse(text)
    assert row.referenceType == 'TEXT_ONLY'
    assert row.reviewRequired and row.calculatedStatus == 'UNKNOWN'


def test_both_sex_specific_rows_survive_without_inferred_sex():
    text = 'Serum Creatinine (Male) 0.9 0.7-1.3 mg/dL / 62-115 µmol/L\nSerum Creatinine (Female) 0.9 0.6-1.1 mg/dL / 53-97 µmol/L\nUric Acid (Male) 4 3-7 mg/dL\nUric Acid (Female) 4 2-6 mg/dL'
    rows = parse(text)
    assert len(rows) == 4
    assert all(r.reviewRequired and r.calculatedStatus == 'NORMAL' for r in rows)
    assert all(r.applicabilityReason == 'PATIENT_SEX_MISSING' for r in rows)
    known = parse('Sex: Female\n' + text)
    assert known[0].calculatedStatus == known[1].calculatedStatus == 'NORMAL'
    assert known[0].applicabilityStatus == 'NOT_APPLICABLE' and known[1].applicabilityStatus == 'APPLICABLE'
    assert len(known) == 4


def test_low_ocr_confidence_prevents_classification_of_clear_dual_range():
    text = 'Total Protein 6.9 6-8.3 g/dL / 60-83 g/L'
    tokens = [OcrToken(word, .4 if word == '6.9' else .99, i*50, 10, 40, 15, 1, (1,1,1)) for i,word in enumerate(text.split())]
    rows = LabRowParser().parse([PageData(1, text, len(text), source='OCR', tokens=tokens)])
    for r in rows: r.sourceType='OCR'
    ValidationEngine().validate(rows)
    assert rows[0].reviewRequired and rows[0].calculatedStatus == 'UNKNOWN'


def test_explicit_metadata_with_blank_demographics_and_same_line_labels():
    text = 'SYNTHETIC HEALTHCARE LABORATORY\nPatient : Example Patient Age :\nReferral : Self DOB :\nBooking Code : SYN-1234567890123456789 Booking Date : 08 Sep 2026'
    data = parse_pathology_metadata([page(text), page(text, 2)], ['Biochemistry', 'Biochemistry'], 2)
    assert data['laboratoryName'] == 'SYNTHETIC HEALTHCARE LABORATORY'
    assert data['patientName'] == 'Example Patient'
    assert data['bookingCode'] == 'SYN-1234567890123456789'
    assert data['bookingDate'] == '08 Sep 2026'
    assert data['age'] is None and data['dateOfBirth'] is None and data['sex'] is None
    assert data['referringDoctor'] == 'Self'
    assert data['reportDate'] is None and data['reportDateIso'] is None
    assert data['sections'] == ['Biochemistry'] and data['pageCount'] == 2
    assert 'results' not in data


def test_conflicts_are_not_arbitrarily_selected_and_only_explicit_report_date_is_used():
    data = parse_pathology_metadata([page('Patient: Example One\nSex: Male\nReport Date: 08 Sep 2026'), page('Patient: Example Two\nSex: Female',2)])
    assert data['patientName'] is None and data['sex'] is None
    assert data['reportDateIso'] == '2026-09-08'
    assert len(data['metadataIssues']) == 2
    ambiguous = parse_pathology_metadata([page('Report Date: 08/09/2026')])
    assert ambiguous['reportDate'] == '08/09/2026' and ambiguous['reportDateIso'] is None


def test_split_logo_suffix_does_not_conflict_with_complete_printed_lab_heading():
    text = 'SYNTHETIC\nHEALTHCARE LABORATORY\nSYNTHETIC HEALTHCARE LABORATORY'
    data = parse_pathology_metadata([page(text)])
    assert data['laboratoryName'] == 'SYNTHETIC HEALTHCARE LABORATORY'
    assert not data['metadataIssues']
    conflict = parse_pathology_metadata([page(text + '\nDIFFERENT HEALTHCARE LABORATORY')])
    assert conflict['laboratoryName'] is None
    assert conflict['metadataIssues'] == [{'code': 'METADATA_CONFLICT', 'field': 'laboratoryName'}]


def test_lab_analysis_response_contains_metadata_and_separate_rows():
    import fitz
    from fastapi.testclient import TestClient
    from main import app
    pdf = fitz.open(); sheet = pdf.new_page()
    sheet.insert_text((40,40), 'SYNTHETIC HEALTHCARE LABORATORY\nPatient: Example Patient Age:\nDOB:\nBooking Code: SYN-12345\nBooking Date: 08 Sep 2026\nBiochemistry\nTest Name Value Reference Range\nTotal Protein 6.9 6-8.3 g/dL / 60-83 g/L')
    response = TestClient(app).post('/analyse', files={'file':('synthetic.pdf',pdf.tobytes(),'application/pdf')}); pdf.close()
    assert response.status_code == 200
    data = response.json()
    assert data['documentType'] == 'LAB_REPORT'
    assert data['structuredData']['patientName'] == 'Example Patient'
    assert data['structuredData']['age'] is None and data['structuredData']['dateOfBirth'] is None
    assert len(data['results']) == 1  # The explicitly labelled booking code is metadata, not a test.
    assert any(r['originalName']=='Total Protein' and r['calculatedStatus']=='NORMAL' for r in data['results'])
    assert 'results' not in data['structuredData']
