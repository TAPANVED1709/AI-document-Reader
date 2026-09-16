import pytest
from core.extractor import PageData, OcrToken
from parsers.pathology_metadata import parse_pathology_metadata
from parsers.lab_parser import LabRowParser


def metadata(text):
    return parse_pathology_metadata([PageData(1, text, len(text))])


@pytest.mark.parametrize('labels,field,value', [
    ('Patient Name|Patient|Name|Pt. Name', 'patientName', 'Example Patient'),
    ('Patient ID|Patient No|UHID|MRN|Registration No', 'patientId', 'SYN-123'),
    ('Age|Patient Age', 'age', '22 Years'),
    ('Gender|Sex|Gender/Sex', 'sex', 'Female'),
    ('DOB|D.O.B.|Date of Birth', 'dateOfBirth', '07-Mar-2004'),
    ('Laboratory|Laboratory Name|Lab Name', 'laboratoryName', 'Synthetic Laboratory'),
    ('Report Date|Reported Date|Reported On|Generated Date|Report Generated Date|Result Date', 'reportDate', '16-Sep-2026'),
    ('Report No|Report Number|Lab No', 'reportNumber', 'R-123'),
    ('Booking Code|Booking No', 'bookingCode', 'B-123'),
    ('Accession No|Accession Number', 'accessionNumber', 'A-123'),
    ('Collection Date|Collected On|Sample Collected', 'collectionDate', '15-Sep-2026'),
    ('Collection Time', 'collectionTime', '08:20'),
    ('Referring Doctor|Referred By|Consultant', 'referringDoctor', 'Dr Example'),
    ('Specimen|Specimen Type|Sample Type', 'specimenType', 'Serum'),
])
def test_all_header_aliases(labels, field, value):
    for label in labels.split('|'):
        data = metadata(f'{label}: {value}')
        assert data[field] == value, label


@pytest.mark.parametrize('label', ['Age/Sex', 'Age/Gender'])
@pytest.mark.parametrize('printed,age,unit,sex', [('22 / Male',22,None,'MALE'), ('22/M',22,None,'MALE'), ('22Y/M',22,'YEARS','MALE'), ('22 Years / Female',22,'YEARS','FEMALE'), ('22/F',22,None,'FEMALE'), ('6 Months/F',6,'MONTHS','FEMALE')])
def test_combined_age_sex(label, printed, age, unit, sex):
    patient = metadata(f'{label}: {printed}')['patient']
    assert (patient['age'],patient['ageUnit'],patient['sex']) == (age,unit,sex)


def test_all_fields_and_dates_are_distinct_and_normalized():
    text = 'Patient Name: Example Patient  Patient ID: P123\nAge/Sex: 22Y/F\nDOB: 07-Mar-2004\nLab Name: Synthetic Laboratory\nReport Number: R123\nBooking Code: B123\nAccession Number: A123\nBooking Date: 14-Sep-2026\nCollected On: 15-Sep-2026 08:20\nReport Generated Date: 16-Sep-2026 09:30\nReferring Doctor: Dr Example\nSpecimen: Serum'
    data = metadata(text)
    assert data['patient'] == dict(name='Example Patient',patientId='P123',age=22,ageUnit='YEARS',sex='FEMALE',dateOfBirth='2004-03-07')
    assert data['report'] == dict(laboratoryName='Synthetic Laboratory',reportNumber='R123',bookingCode='B123',accessionNumber='A123',bookingDate='2026-09-14',collectionDate='2026-09-15',collectionTime='08:20',reportGeneratedDate='2026-09-16',referringDoctor='Dr Example',specimenType='Serum',pageCount=1)
    assert data['rawFields']['dateOfBirth'] == ['07-Mar-2004']
    assert not LabRowParser().parse([PageData(1,text,len(text))])
    assert 'results' not in data


@pytest.mark.parametrize('text', ['', 'Patient: Ms Example Patient', 'Sex: Male or Female', 'Age/Sex: 22/Male or Female', 'Sex: Male\nGender: Female'])
def test_missing_or_ambiguous_sex_is_never_inferred(text):
    data = metadata(text)
    assert data['patient']['sex'] is None
    assert data['report']['reportGeneratedDate'] is None
    if not text:
        assert all(v is None for v in data['patient'].values())
        assert all(v is None for k,v in data['report'].items() if k != 'pageCount')


def test_booking_collection_never_supply_generated_date():
    data=metadata('Booking Date: 2026-09-14\nCollection Date: 2026-09-15\nCollection Time: 8:20 PM')
    assert data['report']['bookingDate']=='2026-09-14'
    assert data['report']['collectionDate']=='2026-09-15'
    assert data['report']['collectionTime']=='20:20'
    assert data['report']['reportGeneratedDate'] is None and data['reportDateIso'] is None


def test_ambiguous_dates_conflicts_and_invalid_dates_are_preserved_without_guessing():
    data=metadata('DOB: 07/03/2004\nReport Date: 31-Feb-2026\nCollection Date: 15-Sep-2026 08:20\nCollection Time: 09:20')
    assert data['patient']['dateOfBirth'] is None
    assert data['report']['reportGeneratedDate'] is None
    assert data['report']['collectionTime'] is None
    assert data['rawFields']['dateOfBirth'] == ['07/03/2004']
    assert metadata('Report Date: 16/09/2026')['report']['reportGeneratedDate'] == '2026-09-16'
    assert metadata('Age: 22Y\nPatient Age: 22 Years\nSex: M\nGender: Male')['patient']['sex']=='MALE'


def test_low_confidence_ocr_headers_cannot_set_patient_demographics():
    text='Patient: Example\nAge/Sex: 22Y/M'
    page=PageData(1,text,len(text),source='OCR',tokens=[OcrToken('22Y/M',.4,0,0,20,10,1,(1,1,1))])
    assert parse_pathology_metadata([page])['patient']['sex'] is None


def test_test_name_label_is_not_patient_identity_and_unsafe_age_is_not_numeric():
    data=metadata('Test Name: Hemoglobin\nAge: ' + '9'*400)
    assert data['patient']['name'] is None and data['patient']['age'] is None
    assert data['rawFields']['age'] == ['9'*400]


def test_synthetic_pdf_analysis_returns_nested_headers_and_qualitative_result():
    import fitz
    from fastapi.testclient import TestClient
    from main import app
    pdf=fitz.open(); sheet=pdf.new_page()
    sheet.insert_text((40,40),'LABORATORY REPORT\nPatient Name: Example Patient\nPatient ID: P123\nAge/Sex: 22Y/M\nDOB: 07-Mar-2004\nReport Date: 16-Sep-2026\nCollected On: 15-Sep-2026 08:20\nSpecimen: Serum\nTest Name Result Unit Reference Range\nHemoglobin 14 g/dL 13-17\nHBsAg Non-Reactive')
    response=TestClient(app).post('/analyse',files={'file':('synthetic.pdf',pdf.tobytes(),'application/pdf')});pdf.close()
    assert response.status_code==200
    data=response.json(); assert data['structuredData']['patient']['patientId']=='P123'
    assert data['structuredData']['report']['reportGeneratedDate']=='2026-09-16'
    row=next(r for r in data['results'] if r['normalizedName']=='HBsAg')
    assert row['value'] is None and row['valueText']=='Non-Reactive'
