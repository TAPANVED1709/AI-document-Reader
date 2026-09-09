from document_types import DISCHARGE, PRESCRIPTION, RADIOLOGY, UNKNOWN, classify_document
from parsers.document_parsers import parse_discharge, parse_prescription, parse_radiology
import pytest

def test_classifier_is_conservative():
    assert classify_document("Laboratory Report\nTest Result\nReference Range").document_type == "LAB_REPORT"
    assert classify_document("Discharge Summary\nHospital Course\nFollow Up").document_type == DISCHARGE
    assert classify_document("Rx\nMedicine\nDose\nFrequency").document_type == PRESCRIPTION
    assert classify_document("CT Brain\nFindings\nImpression\nTechnique").document_type == RADIOLOGY
    assert classify_document("some unrelated notes").document_type == UNKNOWN

def test_discharge_preserves_document_stated_diagnosis_and_medication():
    data = parse_discharge("City Hospital\nDate of Admission: 01/09/2026\nDate of Discharge: 05/09/2026\nDiagnosis: Community acquired pneumonia\nDischarge Medications:\nTab Metformin 500 mg 1-0-1 After food 30 days")
    assert data["documentStatedDiagnosis"] == "Community acquired pneumonia"
    assert "systemDiagnosis" not in data
    assert data["dischargeMedications"][0]["medicineName"] == "Metformin"

def test_prescription_preserves_dose_frequency_and_no_indication():
    data = parse_prescription("Prescription\nTab Metformin 500 mg\n1-0-1\nAfter food\n30 days")
    item = data["medications"][0]
    assert (item["medicineName"], item["strength"], item["dosePattern"], item["originalFrequency"], item["timing"], item["duration"]) == ("Metformin", "500 mg", "1-0-1", "1-0-1", "After food", "30 days")
    assert "indication" not in item

def test_radiology_keeps_findings_and_impression_separate():
    data = parse_radiology("CT Brain\nFindings:\nNo intracranial hemorrhage.\nImpression:\nNo acute intracranial abnormality.")
    assert data["modality"] == "CT"
    assert data["bodyRegion"] == "Brain"
    assert data["findings"] == "No intracranial hemorrhage."
    assert data["impression"] == "No acute intracranial abnormality."
    assert "systemDiagnosis" not in data

@pytest.mark.parametrize("kind,text", [
    (DISCHARGE, "Discharge Summary\nDiagnosis: Stable\nHospital Course: Observed"),
    (DISCHARGE, "Discharge Summary\nPage 1\nHospital Course: Stable\nPage 2\nFollow Up: Clinic"),
    (PRESCRIPTION, "Prescription\nMetformin 500 mg 1-0-1"),
    (PRESCRIPTION, "Rx\nMetformin 500 mg\nBD"),
    (PRESCRIPTION, "Prescription\nMedicine unknown dose"),
    (PRESCRIPTION, "Prescription\nMetformin 500 mg 1-0-1"),
    (PRESCRIPTION, "Prescription\nMetformin 500 mg BD"),
    (PRESCRIPTION, "Prescription\nMetformin 500 mg TDS"),
    (RADIOLOGY, "X-Ray Chest\nFindings: Clear lungs\nImpression: No acute finding"),
    (RADIOLOGY, "CT Brain\nFindings: No hemorrhage\nImpression: Normal"),
    (RADIOLOGY, "MRI Knee\nFindings: No tear\nImpression: Unremarkable"),
    (RADIOLOGY, "Ultrasound Abdomen\nFindings: No focal lesion\nImpression: Unremarkable"),
    (UNKNOWN, "General medical note without document headings"),
    (DISCHARGE, "Discharge Summary\nDischarge Medication: Metformin 500 mg\n1-0-1"),
    (PRESCRIPTION, "Prescription\nMetforrnin ???"),
])
def test_synthetic_document_fixture_routes_conservatively(kind, text):
    assert classify_document(text).document_type == kind
