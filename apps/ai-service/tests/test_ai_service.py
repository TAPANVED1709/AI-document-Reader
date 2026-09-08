"""
Unit and integration tests for AI Document Intelligence Service.
"""
import io
import pytest
from fastapi.testclient import TestClient
import fitz

from main import app
from core.extractor import PdfExtractor, PageData
from parsers.lab_parser import LabRowParser


client = TestClient(app)


def create_synthetic_lab_pdf() -> bytes:
    """Helper to generate a valid synthetic text-based pathology report PDF."""
    doc = fitz.open()
    page = doc.new_page(width=612, height=792)  # Standard Letter

    content = (
        "METROPOLITAN PATHOLOGY LABORATORY\n"
        "PATIENT REPORT: SYNTHETIC BENCHMARK\n"
        "--------------------------------------------------\n"
        "Test Name              Result   Unit       Reference Range\n"
        "--------------------------------------------------\n"
        "Hemoglobin             10.8     g/dL       13.0 - 17.0\n"
        "WBC                    7.4      x10³/uL    4.0 - 11.0\n"
        "Vitamin B12            170      pg/mL      200 - 900\n"
        "HbA1c                  6.8      %          4.0 - 5.6\n"
        "Total Cholesterol      220      mg/dL      < 200\n"
        "Platelets              250      x10³/uL    150 - 450\n"
        "--------------------------------------------------\n"
        "Report verified by Laboratory Director.\n"
    )

    page.insert_text((50, 72), content, fontsize=11)
    pdf_bytes = doc.tobytes()
    doc.close()
    return pdf_bytes


def create_blank_pdf() -> bytes:
    """Helper to create a blank PDF containing no text."""
    doc = fitz.open()
    doc.new_page(width=612, height=792)
    pdf_bytes = doc.tobytes()
    doc.close()
    return pdf_bytes


# 1. Health Endpoint Tests
def test_health_endpoint():
    response = client.get("/health")
    assert response.status_code == 200
    data = response.json()
    assert data["status"] == "Healthy"
    assert data["service"] == "ai-service"
    assert "version" in data


# 2. Lab Row Parser Unit Tests
def test_parser_sample_rows():
    parser = LabRowParser()
    sample_text = (
        "Hemoglobin     10.8     g/dL      13.0 - 17.0\n"
        "WBC            7.4      x10³/uL    4.0 - 11.0\n"
        "Vitamin B12    170      pg/mL      200 - 900\n"
        "HbA1c          6.8      %          4.0 - 5.6\n"
    )
    pages = [PageData(page_number=1, text=sample_text, character_count=len(sample_text))]
    results = parser.parse(pages)

    assert len(results) == 4

    # Test 1: Hemoglobin
    hb = next(r for r in results if r.originalName == "Hemoglobin")
    assert hb.normalizedName == "Hemoglobin"
    assert hb.value == 10.8
    assert hb.unit == "g/dL"
    assert hb.referenceMin == 13.0
    assert hb.referenceMax == 17.0
    assert hb.referenceText == "13.0 - 17.0"

    # Test 2: WBC
    wbc = next(r for r in results if r.originalName == "WBC")
    assert wbc.normalizedName == "WBC"
    assert wbc.value == 7.4
    assert "uL" in wbc.unit
    assert wbc.referenceMin == 4.0
    assert wbc.referenceMax == 11.0

    # Test 3: Vitamin B12
    b12 = next(r for r in results if r.originalName == "Vitamin B12")
    assert b12.normalizedName == "Vitamin B12"
    assert b12.value == 170.0
    assert b12.unit == "pg/mL"
    assert b12.referenceMin == 200.0
    assert b12.referenceMax == 900.0

    # Test 4: HbA1c
    a1c = next(r for r in results if r.originalName == "HbA1c")
    assert a1c.normalizedName == "HbA1c"
    assert a1c.value == 6.8
    assert a1c.unit == "%"
    assert a1c.referenceMin == 4.0
    assert a1c.referenceMax == 5.6


def test_parser_inequality_reference_ranges():
    parser = LabRowParser()
    sample_text = (
        "Total Cholesterol 220 mg/dL < 200\n"
        "eGFR 65 mL/min > 60\n"
    )
    pages = [PageData(page_number=1, text=sample_text, character_count=len(sample_text))]
    results = parser.parse(pages)

    assert len(results) == 2
    chol = next(r for r in results if "Cholesterol" in r.originalName)
    assert chol.value == 220.0
    assert chol.referenceMin is None
    assert chol.referenceMax == 200.0

    egfr = next(r for r in results if "eGFR" in r.originalName)
    assert egfr.value == 65.0
    assert egfr.referenceMin == 60.0
    assert egfr.referenceMax is None


# 3. PDF Extractor Tests
def test_extractor_synthetic_pdf():
    pdf_bytes = create_synthetic_lab_pdf()
    pages, requires_ocr = PdfExtractor.extract_from_bytes(pdf_bytes)

    assert len(pages) == 1
    assert requires_ocr is False
    assert "Hemoglobin" in pages[0].text
    assert pages[0].character_count > 100


def test_extractor_scanned_or_empty_pdf():
    blank_bytes = create_blank_pdf()
    pages, requires_ocr = PdfExtractor.extract_from_bytes(blank_bytes)

    assert len(pages) == 1
    assert requires_ocr is True


# 4. End-to-End /analyse Endpoint Tests
def test_analyse_endpoint_with_valid_pdf():
    pdf_bytes = create_synthetic_lab_pdf()
    response = client.post(
        "/analyse",
        files={"file": ("lab_report.pdf", io.BytesIO(pdf_bytes), "application/pdf")}
    )
    assert response.status_code == 200
    data = response.json()
    assert data["requiresOcr"] is False
    assert len(data["pages"]) == 1
    assert len(data["results"]) >= 4

    names = [r["originalName"] for r in data["results"]]
    assert "Hemoglobin" in names
    assert "WBC" in names


def test_analyse_endpoint_with_blank_pdf():
    blank_bytes = create_blank_pdf()
    response = client.post(
        "/analyse",
        files={"file": ("scanned_report.pdf", io.BytesIO(blank_bytes), "application/pdf")}
    )
    assert response.status_code == 200
    data = response.json()
    assert data["requiresOcr"] is True
    assert len(data["results"]) == 0


def test_analyse_endpoint_rejects_non_pdf():
    response = client.post(
        "/analyse",
        files={"file": ("test.txt", io.BytesIO(b"Hello world"), "text/plain")}
    )
    assert response.status_code == 400
    assert "Unsupported file format" in response.json()["detail"]


def test_analyse_endpoint_rejects_empty_file():
    response = client.post(
        "/analyse",
        files={"file": ("empty.pdf", io.BytesIO(b""), "application/pdf")}
    )
    assert response.status_code == 400
    assert "empty" in response.json()["detail"]
