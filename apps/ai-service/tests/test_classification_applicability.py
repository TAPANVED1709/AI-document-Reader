import pytest
from core.extractor import PageData
from parsers.lab_parser import LabRowParser
from parsers.pathology_metadata import parse_pathology_metadata
from validation import ValidationEngine


def parse(text):
    rows = LabRowParser().parse([PageData(1, text, len(text))])
    for row in rows: row.sourceType = 'NATIVE_TEXT'
    ValidationEngine().validate(rows)
    return rows


@pytest.mark.parametrize('metadata,applicability,reason', [
    ('Patient: Example Patient\nAge: 22\nDOB: 07-Mar-2004', 'REVIEW_REQUIRED', 'PATIENT_SEX_MISSING'),
    ('Sex: Male', 'APPLICABLE', None),
    ('Sex: Female', 'NOT_APPLICABLE', 'PATIENT_SEX_MISMATCH'),
    ('Sex: Male\nSex: Female', 'REVIEW_REQUIRED', 'PATIENT_SEX_MISSING'),
    ('Sex: Male or Female', 'REVIEW_REQUIRED', 'PATIENT_SEX_MISSING'),
])
def test_creatinine_numeric_comparison_independent_of_demographics(metadata, applicability, reason):
    text = metadata + '\nSerum Creatinine (Male) 0.7 0.7-1.3 mg/dl / 62-115 µmol/L'
    row, = parse(text)
    assert (row.originalName, row.normalizedName, row.demographicQualifier) == ('Serum Creatinine (Male)', 'Creatinine', 'MALE')
    assert (row.value, row.normalizedUnit, row.referenceMin, row.referenceMax) == (.7, 'mg/dL', .7, 1.3)
    assert row.calculatedStatus == 'NORMAL'
    assert (row.applicabilityStatus, row.applicabilityReason) == (applicability, reason)
    assert row.reviewRequired == (applicability != 'APPLICABLE')
    assert row.referenceText == '0.7-1.3 mg/dl / 62-115 µmol/L'


@pytest.mark.parametrize('suffix,qualifier,status', [('(Male)', 'MALE', 'NORMAL'), ('Female', 'FEMALE', 'NORMAL'), ('(18-65 years)', 'AGE_RANGE', 'APPLICABLE'), ('(Adult)', 'ADULT', 'REVIEW_REQUIRED'), ('(Pediatric)', 'PEDIATRIC', 'REVIEW_REQUIRED'), ('(Pregnancy)', 'PREGNANCY', 'REVIEW_REQUIRED')])
def test_qualifiers_are_structural_and_no_age_cutoff_is_invented(suffix, qualifier, status):
    row, = parse(f'Age: 22 years\nSerum Creatinine {suffix} 0.7 mg/dL 0.7-1.3')
    assert row.normalizedName == 'Creatinine' and row.demographicQualifier == qualifier
    assert row.calculatedStatus == 'NORMAL'
    assert row.applicabilityStatus == ('REVIEW_REQUIRED' if status == 'NORMAL' else status)


def test_distinct_demographic_variants_keep_distinct_numeric_statuses():
    male, female = parse('Hemoglobin (Male) 14 13-17 g/dl / 130-170 g/l\nHemoglobin (Female) 16 12-15 g/dl / 120-150 g/l')
    assert male.calculatedStatus == 'NORMAL' and female.calculatedStatus == 'HIGH'
    assert female.demographicQualifier == 'FEMALE'
    assert all(r.reviewRequired and r.applicabilityStatus == 'REVIEW_REQUIRED' for r in [male, female])


@pytest.mark.parametrize('text,name,value,unit,low,high,status', [
    ('Alpha-1 Antitrypsin 70 90-200 mg/dL / 0-0 mg/dL', 'Alpha-1 Antitrypsin', 70, 'mg/dL', 90, 200, 'LOW'),
    ('Total Leukocyte Count (TLC) 5000 4000-11000 /µL / 4-11 ^9/L', 'WBC', 5000, '/µL', 4000, 11000, 'NORMAL'),
    ('Platelet Count 160000 150000-450000 /µL / 150-450 x10^9/L', 'Platelet Count', 160000, '/µL', 150000, 450000, 'NORMAL'),
])
def test_numeric_test_names_count_units_and_secondary_placeholder(text, name, value, unit, low, high, status):
    row, = parse(text)
    assert (row.normalizedName, row.value, row.unit, row.referenceMin, row.referenceMax, row.calculatedStatus) == (name, value, unit, low, high, status)
    if '0-0' in text:
        assert row.reviewRequired and any(i.code == 'SECONDARY_RANGE_PLACEHOLDER' for i in row.validationIssues)
    assert row.referenceText in text


def test_name_confidence_does_not_erase_numeric_comparison_but_numeric_uncertainty_does():
    row, = parse('Bicarbonate (HCO3-) 25 22-28 mEq/L / 22-28 mmol/L')
    assert row.confidence == .7 and row.reviewRequired and row.calculatedStatus == 'NORMAL'
    row.fieldConfidences['value'] = .4
    ValidationEngine().validate([row])
    assert row.calculatedStatus == 'UNKNOWN' and row.reviewRequired


def test_wrapped_names_keep_original_text_without_absorbing_adjacent_results():
    rows = parse('Activated Partial Thromboplastin Time\n(aPTT)\n25 sec 25-40\nMean Corpuscular Hemoglobin\nConcentration (MCHC)\n35 g/dL 32-36\nErythrocyte Sedimentation Rate (ESR)\n(Male)\n12 mm/hr 0-15\nCreatinine\nGlucose 90 mg/dL 70-100')
    assert len(rows) == 4
    assert rows[0].originalName == 'Activated Partial Thromboplastin Time (aPTT)'
    assert rows[1].originalName == 'Mean Corpuscular Hemoglobin Concentration (MCHC)'
    assert rows[2].demographicQualifier == 'MALE'
    assert rows[3].originalName == 'Glucose'


def test_printed_age_and_dob_do_not_establish_sex():
    text = 'Patient: Example Patient\nAge: 22\nDOB: 07-Mar-2004\nHemoglobin (Female) 16 g/dL 12-15'
    data = parse_pathology_metadata([PageData(1, text, len(text))])
    assert data['age'] is not None and data['dateOfBirth'] == '07-Mar-2004'
    assert data['sex'] is None
    assert parse(text)[0].applicabilityReason == 'PATIENT_SEX_MISSING'


@pytest.mark.parametrize('reference,status', [('< 10', 'HIGH'), ('<= 10', 'NORMAL'), ('> 10', 'LOW'), ('>= 10', 'NORMAL')])
def test_printed_one_sided_boundaries(reference, status):
    row, = parse(f'Example Test 10 mg/dL {reference}')
    assert row.calculatedStatus == status


def test_censored_value_and_conflicting_same_unit_ranges_remain_unresolved():
    for text in ['Glucose <90 mg/dL 70-100', 'Sodium 140 135-145 mmol/L / 120-130 mmol/L']:
        row, = parse(text)
        assert row.calculatedStatus == 'UNKNOWN' and row.reviewRequired
