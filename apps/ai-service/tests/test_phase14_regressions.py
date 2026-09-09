"""Regressions from persisted Phase 14 pilot failures (frozen synthetic PDFs)."""
from pathlib import Path
import pytest
from core.extractor import PdfExtractor, PageData
from core.ocr_engine import LocalOcrEngine, is_tesseract_available
from parsers.lab_parser import LabRowParser
from validation.validator import ValidationEngine

FIXTURES=Path(__file__).resolve().parents[3]/"tests/pilot/fixtures"


def parse(text):
    values=LabRowParser().parse([PageData(1,text,len(text))])
    for r in values: r.sourceType="NATIVE_TEXT"
    ValidationEngine().validate(values)
    return values


def test_page_counter_never_becomes_result():
    assert len(parse("Page 1 of 3\nHemoglobin 10.8 g/dL 13 - 17"))==1


def test_native_three_column_cells_keep_own_ranges():
    pages,_=PdfExtractor.extract_from_bytes((FIXTURES/"pilot-03.pdf").read_bytes())
    values=LabRowParser().parse(pages)
    assert len(values)==12
    assert all(r.referenceType=="BETWEEN" for r in values)
    assert next(r for r in values if r.normalizedName=="MCHC").referenceMax==36


def test_wrapped_rows_consumed_once_and_reference_completed():
    pages,_=PdfExtractor.extract_from_bytes((FIXTURES/"pilot-09.pdf").read_bytes())
    values=LabRowParser().parse(pages)
    assert len(values)==20
    assert sum(r.normalizedName=="Platelet Count" for r in values)==1
    assert next(r for r in values if r.normalizedName=="MCHC").referenceMax==36
    assert not any(r.originalName in {"Count","Protein"} for r in values)


def test_conflicting_metadata_does_not_select_first_sex():
    r=parse("Sex: Male   Sex: Female\nHemoglobin 10.8 g/dL Male: 13 - 17; Female: 12 - 15")[0]
    assert r.referenceType=="TEXT_ONLY" and r.referenceMin is None and r.reviewRequired


def test_month_bounds_use_month_units():
    r=parse("Age: 8 months\nHemoglobin 10.8 g/dL 0-6 months: 9 - 13; 7-12 months: 10 - 14")[0]
    assert (r.referenceMin,r.referenceMax)==(10,14)
    assert not r.reviewRequired


def test_biochemistry_and_metabolic_sections_do_not_prepend_names():
    values=parse("Metabolic\nMagnesium 2.1 mg/dL 1.7 - 2.4\nBiochemistry\nGlucose 92 mg/dL 70 - 99")
    assert [(r.originalName,r.section) for r in values]==[("Magnesium","Metabolic"),("Glucose","Biochemistry")]


def test_unknown_numeric_range_is_reviewed_not_trusted_as_text():
    r=parse("MCHC 33.2 g/dL 31 - 36 Neutrophils")[0]
    assert r.reviewRequired


@pytest.mark.skipif(not is_tesseract_available(),reason="Local Tesseract unavailable")
def test_corrupted_section_heading_does_not_propagate_previous_panel():
    pages=LocalOcrEngine.ocr_from_bytes((FIXTURES/"pilot-30.pdf").read_bytes(),[1])
    values=LabRowParser().parse(pages)
    for r in values:
        if r.normalizedName in {"Ferritin","TIBC"}:
            assert r.section=="Iron Studies" and r.reviewRequired


@pytest.mark.skipif(not is_tesseract_available(),reason="Local Tesseract unavailable")
def test_parallel_ocr_panels_keep_rows_and_sections():
    pages=LocalOcrEngine.ocr_from_bytes((FIXTURES/"pilot-12.pdf").read_bytes())
    values=LabRowParser().parse(pages)
    assert len(values)==16
    assert next(r for r in values if r.normalizedName=="Lymphocytes").value==29
    assert next(r for r in values if r.normalizedName=="LDL").section=="Lipid Profile"
    assert all(r.referenceType!="TEXT_ONLY" for r in values)
