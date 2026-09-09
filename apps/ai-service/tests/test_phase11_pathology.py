"""Synthetic Phase 11 pathology benchmark; these are development metrics only."""
import json

from core.extractor import OcrToken, PageData
from parsers.lab_parser import LabRowParser


FIXTURE = """CBC
Hb 10.8 g / dL 13.0 - 17.0 L
RBC 4.5 x10^3/uL 4.0 - 6.0
WBC 7.2 K/uL 4.0 - 11.0
Platelet count 220 x10^3/uL 150 - 450
LFT
SGOT 32 U/L 5 - 40
SGPT 28 U/L 7 - 56
Total bilirubin 0.8 mg/dl 0.2 - 1.2
GGT 24 U/L 9 - 48
RFT
Creatlnine 0.9 mg/dl 0.7 - 1.3
eGFR 95 mL/min > 60
Lipid Profile
HDL 52 mg/dl > 40
Triglycerides 140 mg/dl 0 - 150
Diabetes Profile
Fasting glucose 96 mg/dl 70 - 99
HbA1c 6.8 % 4.0 - 5.6 H
Thyroid Profile
TSH 2.1 µIU/mL 0.4 - 4.0
Iron Studies
Ferritin 80 ng/mL 20 - 250
Urine Routine
Protein Negative Negative
HIV screening Non-Reactive Non-Reactive
Method: HPLC
"""


def test_phase11_synthetic_benchmark_recovers_common_pathology_rows():
    results = LabRowParser().parse([PageData(1, FIXTURE, len(FIXTURE), False, "NATIVE_TEXT")])
    by_name = {r.normalizedName: r for r in results}
    expected = {
        "Hemoglobin": (10.8, "g/dL", 13.0, 17.0),
        "RBC": (4.5, "10³/µL", 4.0, 6.0),
        "WBC": (7.2, "10³/µL", 4.0, 11.0),
        "AST": (32.0, "U/L", 5.0, 40.0),
        "ALT": (28.0, "U/L", 7.0, 56.0),
        "Creatinine": (0.9, "mg/dL", 0.7, 1.3),
        "HbA1c": (6.8, "%", 4.0, 5.6),
        "Ferritin": (80.0, "ng/mL", 20.0, 250.0),
    }
    assert set(expected) <= set(by_name)
    assert all((by_name[k].value, by_name[k].normalizedUnit, by_name[k].referenceMin, by_name[k].referenceMax) == v for k, v in expected.items())
    assert by_name["Hemoglobin"].originalName == "Hb"
    assert by_name["Creatinine"].originalName == "Creatlnine"
    assert by_name["Protein"].value is None and by_name["Protein"].valueText == "Negative"
    assert by_name["HIV"].value is None and by_name["HIV"].valueText == "Non-Reactive"
    assert by_name["HbA1c"].reportedFlag == "H" and by_name["HbA1c"].referenceMin == 4.0
    assert by_name["HbA1c"].section == "Diabetes Profile"
    assert by_name["AST"].section == "Liver Function Test"


def test_phase11_ocr_metadata_confidence_and_bounding_box_are_preserved():
    line = "HemogIobin 10.8 g/dL 13.0 - 17.0"
    tokens = [OcrToken("HemogIobin", 0.92, 10, 20, 80, 18, 1, (1, 1)), OcrToken("10.8", 0.42, 100, 20, 35, 18, 1, (1, 2)), OcrToken("g/dL", 0.95, 145, 20, 35, 18, 1, (1, 3)), OcrToken("13.0", 0.95, 190, 20, 35, 18, 1, (1, 4)), OcrToken("-", 0.95, 230, 20, 8, 18, 1, (1, 5)), OcrToken("17.0", 0.95, 245, 20, 35, 18, 1, (1, 6))]
    result = LabRowParser().parse([PageData(1, line, len(line), True, "OCR", tokens)])[0]
    box = json.loads(result.boundingBoxJson)
    assert result.originalName == "HemogIobin" and result.normalizedName == "Hemoglobin"
    assert result.value == 10.8 and result.confidence == 0.42 and result.reviewRequired
    assert box["x"] == 10 and box["y"] == 20 and box["width"] > 0 and box["height"] == 18 and box["page"] == 1
    assert box["coordinateSpace"] == "rendered_pixels" and box["dpi"] == 300


def test_phase11_demographic_ranges_require_confident_metadata():
    parser = LabRowParser()
    male = parser.parse([PageData(1, "Age: 30 years\nSex: Male\nHemoglobin 14 g/dL Male: 13-17 Female: 12-15", 0)])[0]
    unknown = parser.parse([PageData(1, "Hemoglobin 14 g/dL Male: 13-17 Female: 12-15", 0)])[0]
    assert male.referenceMin == 13 and male.referenceMax == 17 and male.referenceText == "Male: 13-17 Female: 12-15"
    assert unknown.referenceType == "TEXT_ONLY" and unknown.referenceMin is None and unknown.reviewRequired


def test_phase11_method_and_comments_do_not_create_false_rows():
    page = PageData(1, "CBC\nMethod: HPLC\nHbA1c 6.8 % 4.0 - 5.6\nSample hemolysed\nRepeat advised\nFasting sample\nReference range revised", 0)
    results = LabRowParser().parse([page])
    assert [r.normalizedName for r in results] == ["HbA1c"]
    assert results[0].methodText == "HPLC"


def test_phase11_advanced_layouts_are_page_and_row_stable():
    page1 = PageData(1, "CBC\nWhite Blood\nCell Count 7.2 K/uL 4.0 -\n11.0\nAST 32 U/L 5 - 40 Method: Enzymatic\nAST 31 U/L 5 - 40 Method: HPLC", 0)
    page2 = PageData(2, "LFT\nALT 28 U/L 7 - 56\nKFT\nCreatinine 0.9 mg/dL 0.7 - 1.3", 0)
    results = LabRowParser().parse([page1, page2])
    names = [r.normalizedName for r in results]
    assert "WBC" in names and names.count("AST") == 2
    assert [(r.normalizedName, r.page) for r in results if r.normalizedName in {"ALT", "Creatinine"}] == [("ALT", 2), ("Creatinine", 2)]
    ast = [r for r in results if r.normalizedName == "AST"]
    assert {r.methodText for r in ast} == {"Enzymatic", "HPLC"}
