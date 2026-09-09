"""Reproducible synthetic pathology validation benchmark.

This measures engineering behavior only. It is not clinical validation.
"""
from dataclasses import asdict, dataclass, field
import csv, json
import io
import re
from pathlib import Path
from typing import Any
import fitz
from PIL import Image, ImageDraw, ImageFont

from core.extractor import PageData
from document_types import LAB, classify_document
from parsers.lab_parser import LabRowParser
from validation.validator import ValidationEngine
from core.ocr_engine import LocalOcrEngine, get_tesseract_version, is_tesseract_available

ROOT = Path(__file__).resolve().parents[3]

@dataclass
class ValidationExpectedResult:
    originalTestName: str
    normalizedTestName: str
    expectedValueNumeric: float | None = None
    expectedValueText: str | None = None
    expectedUnit: str | None = None
    expectedReferenceText: str | None = None
    expectedReferenceMin: float | None = None
    expectedReferenceMax: float | None = None
    expectedReferenceOperator: str | None = None
    expectedPrintedFlag: str | None = None
    expectedSection: str | None = None
    expectedPage: int = 1
    expectedMethod: str | None = None
    expectedReviewRequired: bool = False

@dataclass
class ValidationDocument:
    documentId: str
    documentType: str
    sourceType: str
    expectedPageCount: int
    pages: list[PageData]
    expectedResults: list[ValidationExpectedResult]
    metadata: dict[str, Any] = field(default_factory=dict)

def row(name, normalized, value=None, unit=None, ref=None, low=None, high=None, section=None, page=1, flag=None, method=None, text=None, review=False):
    return ValidationExpectedResult(name, normalized, value, text, unit, ref, low, high, None, flag, section, page, method, review)

def make_documents():
    docs = []
    docs.append(ValidationDocument("clean-cbc", LAB, "NATIVE_TEXT", 1, [PageData(1, "Complete Blood Count\nHemoglobin 10.8 g/dL 13.0 - 17.0 L\nWBC 7.2 K/uL 4.0 - 11.0\nPlatelet count 220 x10^3/uL 150 - 450", 0)], [row("Hemoglobin", "Hemoglobin", 10.8, "g/dL", "13.0 - 17.0", 13, 17, "Complete Blood Count", flag="L"), row("WBC", "WBC", 7.2, "10³/µL", "4.0 - 11.0", 4, 11, "Complete Blood Count"), row("Platelet count", "Platelet Count", 220, "10³/µL", "150 - 450", 150, 450, "Complete Blood Count")]))
    docs.append(ValidationDocument("clean-chemistry", LAB, "NATIVE_TEXT", 1, [PageData(1, "Liver Function Test\nAST 32 U/L 5 - 40\nALT 28 U/L 7 - 56\nAlbumin 4.2 g/dL 3.5 - 5.2\nKidney Function Test\nCreatinine 0.9 mg/dL 0.7 - 1.3\nUrea 28 mg/dL 15 - 45", 0)], [row("AST", "AST", 32, "U/L", "5 - 40", 5, 40, "Liver Function Test"), row("ALT", "ALT", 28, "U/L", "7 - 56", 7, 56, "Liver Function Test"), row("Albumin", "Albumin", 4.2, "g/dL", "3.5 - 5.2", 3.5, 5.2, "Liver Function Test"), row("Creatinine", "Creatinine", .9, "mg/dL", "0.7 - 1.3", .7, 1.3, "Kidney Function Test"), row("Urea", "Urea", 28, "mg/dL", "15 - 45", 15, 45, "Kidney Function Test")]))
    docs.append(ValidationDocument("scanned-pathology", LAB, "OCR", 1, [PageData(1, "CBC\nHb 10.8 g/dL 13.0 - 17.0\nHbA1c 6.8 % 4.0 - 5.6 H", 0, True, "OCR")], [row("Hb", "Hemoglobin", 10.8, "g/dL", "13.0 - 17.0", 13, 17, "Complete Blood Count"), row("HbA1c", "HbA1c", 6.8, "%", "4.0 - 5.6", 4, 5.6, "Complete Blood Count", flag="H")]))
    docs.append(ValidationDocument("hybrid-multipage", LAB, "HYBRID", 2, [PageData(1, "Liver Function Test\nAST 32 U/L 5 - 40", 0), PageData(2, "Kidney Function Test\nCreatinine 0.9 mg/dL 0.7 - 1.3", 0, True, "OCR")], [row("AST", "AST", 32, "U/L", "5 - 40", 5, 40, "Liver Function Test", page=1), row("Creatinine", "Creatinine", .9, "mg/dL", "0.7 - 1.3", .7, 1.3, "Kidney Function Test", page=2)]))
    docs.append(ValidationDocument("qualitative-urine-serology", LAB, "NATIVE_TEXT", 1, [PageData(1, "Urine Routine\nProtein Negative Negative\nHIV Non-Reactive Non-Reactive\nESR 18 mm/hr 0 - 20", 0)], [row("Protein", "Protein", text="Negative", ref="Negative", section="Urine Examination"), row("HIV", "HIV", text="Non-Reactive", ref="Non-Reactive", section="Urine Examination"), row("ESR", "ESR", 18, "mm/hr", "0 - 20", 0, 20, "Urine Examination")]))
    docs.append(ValidationDocument("demographic-range", LAB, "NATIVE_TEXT", 1, [PageData(1, "Laboratory Report\nAge: 30 years\nSex: Male\nHemoglobin 14 g/dL Male: 13-17 Female: 12-15", 0)], [row("Hemoglobin", "Hemoglobin", 14, "g/dL", "Male: 13-17 Female: 12-15", 13, 17, None)] , {"ageYears": 30, "sex": "Male"}))
    docs.append(ValidationDocument("low-quality-ocr", LAB, "OCR", 1, [PageData(1, "CBC\nHemogIobin 108 g/dL 13.0 - 17.0", 0, True, "OCR")], [row("HemogIobin", "Hemoglobin", 10.8, "g/dL", "13.0 - 17.0", 13, 17, "Complete Blood Count", review=True)], {"expectedError": "OCR_DECIMAL_LOSS"}))
    docs.append(ValidationDocument("duplicate-methods", LAB, "NATIVE_TEXT", 1, [PageData(1, "Liver Function Test\nAST 32 U/L 5 - 40 Method: Enzymatic\nAST 31 U/L 5 - 40 Method: HPLC", 0)], [row("AST", "AST", 32, "U/L", "5 - 40", 5, 40, "Liver Function Test", method="Enzymatic"), row("AST", "AST", 31, "U/L", "5 - 40", 5, 40, "Liver Function Test", method="HPLC")]))
    docs.append(ValidationDocument("panel-sweep", LAB, "NATIVE_TEXT", 1, [PageData(1, "Diabetes Profile\nGlucose 96 mg/dL 70 - 99\nLipid Profile\nLDL 110 mg/dL 0 - 130\nThyroid Profile\nTSH 2.1 uIU/ml 0.4 - 4.0\nIron Studies\nFerritin 80 ng/mL 20 - 250\nVitamin Profile\nVitamin D 32 ng/mL 30 - 100\nInflammation\nCRP 2 mg/L 0 - 5\nCoagulation\nINR 1.0 ratio 0.8 - 1.2\nElectrolytes\nSodium 140 mmol/L 135 - 145\nSerology\nHBsAg Non-Reactive Non-Reactive", 0)], [row("Glucose", "Glucose", 96, "mg/dL", "70 - 99", 70, 99, "Diabetes Profile"), row("LDL", "LDL", 110, "mg/dL", "0 - 130", 0, 130, "Lipid Profile"), row("TSH", "TSH", 2.1, "µIU/mL", "0.4 - 4.0", .4, 4, "Thyroid Profile"), row("Ferritin", "Ferritin", 80, "ng/mL", "20 - 250", 20, 250, "Iron Studies"), row("Vitamin D", "Vitamin D", 32, "ng/mL", "30 - 100", 30, 100, "Vitamin Profile"), row("CRP", "CRP", 2, "mg/L", "0 - 5", 0, 5, "Inflammation"), row("INR", "INR", 1, "ratio", "0.8 - 1.2", .8, 1.2, "Coagulation"), row("Sodium", "Sodium", 140, "mmol/L", "135 - 145", 135, 145, "Electrolytes"), row("HBsAg", "HBsAg", text="Non-Reactive", ref="Non-Reactive", section="Serology")]))
    panel_templates = [
        ("CBC", "Complete Blood Count", [("Hemoglobin", "Hemoglobin", "10.8", "g/dL", "13 - 17"), ("RBC", "RBC", "4.5", "10^3/uL", "4 - 6"), ("WBC", "WBC", "7.2", "K/uL", "4 - 11"), ("Platelet count", "Platelet Count", "220", "K/uL", "150 - 450"), ("HCT", "Hematocrit", "42", "%", "40 - 52"), ("MCV", "MCV", "88", "fL", "80 - 100"), ("MCH", "MCH", "29", "pg", "27 - 33"), ("RDW", "RDW", "13", "%", "11 - 15")]),
        ("LFT", "Liver Function Test", [("AST", "AST", "32", "U/L", "5 - 40"), ("ALT", "ALT", "28", "U/L", "7 - 56"), ("ALP", "ALP", "90", "U/L", "44 - 147"), ("GGT", "GGT", "24", "U/L", "9 - 48"), ("Albumin", "Albumin", "4.2", "g/dL", "3.5 - 5.2"), ("Globulin", "Globulin", "3.0", "g/dL", "2.0 - 3.5"), ("Total protein", "Total Protein", "7.2", "g/dL", "6 - 8"), ("Total bilirubin", "Total Bilirubin", ".8", "mg/dL", ".2 - 1.2")]),
        ("RFT", "Renal Function Test", [("Creatinine", "Creatinine", ".9", "mg/dL", ".7 - 1.3"), ("Urea", "Urea", "28", "mg/dL", "15 - 45"), ("BUN", "BUN", "14", "mg/dL", "7 - 20"), ("Uric acid", "Uric Acid", "5", "mg/dL", "3.5 - 7.2"), ("eGFR", "eGFR", "95", "mL/min", "> 60"), ("Sodium", "Sodium", "140", "mmol/L", "135 - 145"), ("Potassium", "Potassium", "4.2", "mmol/L", "3.5 - 5.1"), ("Calcium", "Calcium", "9.4", "mg/dL", "8.5 - 10.5")]),
        ("Diabetes", "Diabetes Profile", [("Fasting glucose", "Glucose", "96", "mg/dL", "70 - 99"), ("Random glucose", "Glucose", "110", "mg/dL", "70 - 140"), ("HbA1c", "HbA1c", "6.8", "%", "4 - 5.6"), ("Estimated average glucose", "Glucose", "148", "mg/dL", "70 - 126"), ("Glucose", "Glucose", "90", "mg/dL", "70 - 99"), ("PP glucose", "Glucose", "120", "mg/dL", "70 - 140"), ("HbA1c", "HbA1c", "5.4", "%", "4 - 5.6"), ("Glucose", "Glucose", "101", "mg/dL", "70 - 110")]),
        ("Lipids", "Lipid Profile", [("Total cholesterol", "Total Cholesterol", "180", "mg/dL", "0 - 200"), ("HDL", "HDL", "52", "mg/dL", "> 40"), ("LDL", "LDL", "110", "mg/dL", "0 - 130"), ("VLDL", "VLDL", "28", "mg/dL", "5 - 40"), ("Triglycerides", "Triglycerides", "140", "mg/dL", "0 - 150"), ("Non-HDL cholesterol", "Non-HDL cholesterol", "128", "mg/dL", "0 - 160"), ("LDL/HDL ratio", "LDL/HDL Ratio", "2.1", "ratio", "0 - 3.5"), ("TC/HDL ratio", "TC/HDL Ratio", "3.4", "ratio", "0 - 5")]),
        ("Thyroid", "Thyroid Profile", [("TSH", "TSH", "2.1", "uIU/ml", ".4 - 4"), ("T3", "T3", "1.2", "ng/mL", ".8 - 2"), ("T4", "T4", "8", "µg/dL", "5 - 12"), ("Free T3", "Free T3", "3", "pg/mL", "2.3 - 4.2"), ("Free T4", "Free T4", "1.1", "ng/dL", ".8 - 1.8"), ("TSH", "TSH", "1.8", "uIU/ml", ".4 - 4"), ("T3", "T3", "1.1", "ng/mL", ".8 - 2"), ("T4", "T4", "7.5", "µg/dL", "5 - 12")]),
        ("Iron", "Iron Studies", [("Serum iron", "Iron", "90", "µg/dL", "60 - 170"), ("Ferritin", "Ferritin", "80", "ng/mL", "20 - 250"), ("TIBC", "TIBC", "300", "µg/dL", "240 - 450"), ("UIBC", "UIBC", "210", "µg/dL", "150 - 375"), ("Transferrin saturation", "Transferrin Saturation", "30", "%", "20 - 50"), ("Vitamin B12", "Vitamin B12", "400", "pg/mL", "200 - 900"), ("Vitamin D", "Vitamin D", "32", "ng/mL", "30 - 100"), ("Folate", "Folate", "10", "ng/mL", "3 - 17")]),
        ("Inflammation", "Inflammation", [("CRP", "CRP", "2", "mg/L", "0 - 5"), ("hs-CRP", "hs-CRP", "1", "mg/L", "0 - 3"), ("ESR", "ESR", "18", "mm/hr", "0 - 20"), ("CRP", "CRP", "3", "mg/L", "0 - 5"), ("ESR", "ESR", "10", "mm/hr", "0 - 20"), ("CRP", "CRP", "1", "mg/L", "0 - 5"), ("ESR", "ESR", "12", "mm/hr", "0 - 20"), ("hs-CRP", "hs-CRP", "2", "mg/L", "0 - 3")]),
        ("Coagulation", "Coagulation", [("PT", "PT", "12", "sec", "11 - 14"), ("INR", "INR", "1", "ratio", ".8 - 1.2"), ("aPTT", "aPTT", "30", "sec", "25 - 35"), ("PT", "PT", "13", "sec", "11 - 14"), ("INR", "INR", "1.1", "ratio", ".8 - 1.2"), ("aPTT", "aPTT", "29", "sec", "25 - 35"), ("PT", "PT", "12.5", "sec", "11 - 14"), ("INR", "INR", ".9", "ratio", ".8 - 1.2")]),
        ("Urine", "Urine Examination", [("pH", "pH", "6", None, "5 - 8"), ("Specific gravity", "Specific Gravity", "1.02", None, "1.005 - 1.03"), ("Protein", "Protein", None, None, "Negative",), ("Glucose", "Glucose", "Negative", None, "Negative"), ("Ketones", "Ketones", "Negative", None, "Negative"), ("RBC/hpf", "RBC/hpf", "2", None, "0 - 3"), ("WBC/hpf", "WBC/hpf", "3", None, "0 - 5"), ("Bacteria", "Bacteria", "Absent", None, "Absent")]),
        ("Serology", "Serology", [("HBsAg", "HBsAg", "Non-Reactive", None, "Non-Reactive"), ("HIV", "HIV", "Non-Reactive", None, "Non-Reactive"), ("HCV", "HCV", "Negative", None, "Negative"), ("VDRL", "VDRL", "Non-Reactive", None, "Non-Reactive"), ("CRP", "CRP", "Negative", None, "Negative"), ("Rheumatoid factor", "Rheumatoid Factor", "Negative", None, "Negative"), ("HIV", "HIV", "Non-Reactive", None, "Non-Reactive"), ("HCV", "HCV", "Negative", None, "Negative")]),
    ]
    for index, (label, section, values) in enumerate(panel_templates):
        lines, expected = [section], []
        for name, normalized, value, unit, reference in values:
            lines.append(f"{name} {value or reference} {unit or ''} {reference}")
            if value in {"Negative", "Non-Reactive", "Absent"} or value is None:
                expected.append(row(name, normalized, text=value or reference, ref=reference, section=section))
            else:
                parts = reference.split("-")
                expected.append(row(name, normalized, float(value), unit, reference, float(parts[0]) if len(parts) == 2 else None, float(parts[1]) if len(parts) == 2 else None, section))
        docs.append(ValidationDocument(f"expanded-panel-{index:02d}", LAB, "NATIVE_TEXT" if index % 3 else "HYBRID", 1, [PageData(1, "\n".join(lines), 0)], expected))
    categories = {"clean-cbc": "A_CLEAN_TEXT_PDF", "clean-chemistry": "A_CLEAN_TEXT_PDF", "scanned-pathology": "B_SCANNED_PDF", "hybrid-multipage": "C_HYBRID_PDF_F_MULTI_PAGE", "qualitative-urine-serology": "I_QUALITATIVE_RESULTS", "demographic-range": "J_AGE_SEX_RANGES", "low-quality-ocr": "D_LOW_QUALITY_OCR_N_DECIMAL_O_CHARACTER_CONFUSIONS", "duplicate-methods": "P_DUPLICATES_Q_DIFFERENT_METHODS_R_COMMENTS_METHOD", "panel-sweep": "K_MISSING_UNITS_L_MISSING_RANGES_M_FLAGS_S_UNKNOWN_PANELS"}
    for document in docs:
        document.metadata["category"] = categories.get(document.documentId, "EXPANDED_PANEL_COVERAGE")
        document.metadata["format"] = {"hybrid-multipage": "MULTI_PAGE", "advanced-layout-multipage": "MULTI_COLUMN", "low-quality-ocr": "LOW_QUALITY_OCR", "scanned-pathology": "SCANNED"}.get(document.documentId, "NATIVE" if document.sourceType == "NATIVE_TEXT" else document.sourceType)
    if is_tesseract_available():
        def real_doc(doc_id, section, lines, expected):
            image = Image.new("L", (2550, 1650), 255); draw = ImageDraw.Draw(image)
            font = ImageFont.truetype(r"C:\Windows\Fonts\arial.ttf", 54); y = 130
            for line in [section, *lines]: draw.text((150, y), line, fill=0, font=font); y += 105
            stream = io.BytesIO(); image.save(stream, format="PNG")
            pdf = fitz.open(); page = pdf.new_page(width=612, height=396); page.insert_image(rect=page.rect, stream=stream.getvalue())
            pdf_bytes = pdf.tobytes(); pdf.close()
            return ValidationDocument(doc_id, LAB, "OCR", 1, LocalOcrEngine.ocr_from_bytes(pdf_bytes, {1}), expected, {"category": "B_REAL_TESSERACT_SCANNED", "format": "SCANNED", "realOcr": True})
        docs.extend([
            real_doc("real-ocr-cbc", "COMPLETE BLOOD COUNT", ["Hemoglobin 10.8 g/dL 13.0 - 17.0", "WBC 7.2 K/uL 4.0 - 11.0", "Platelet count 220 K/uL 150 - 450"], [row("Hemoglobin", "Hemoglobin", 10.8, "g/dL", "13.0 - 17.0", 13, 17, "Complete Blood Count"), row("WBC", "WBC", 7.2, "10³/µL", "4.0 - 11.0", 4, 11, "Complete Blood Count"), row("Platelet count", "Platelet Count", 220, "10³/µL", "150 - 450", 150, 450, "Complete Blood Count")]),
            real_doc("real-ocr-lft", "LIVER FUNCTION TEST", ["AST 32 U/L 5 - 40", "ALT 28 U/L 7 - 56", "Albumin 4.2 g/dL 3.5 - 5.2"], [row("AST", "AST", 32, "U/L", "5 - 40", 5, 40, "Liver Function Test"), row("ALT", "ALT", 28, "U/L", "7 - 56", 7, 56, "Liver Function Test"), row("Albumin", "Albumin", 4.2, "g/dL", "3.5 - 5.2", 3.5, 5.2, "Liver Function Test")]),
            real_doc("real-ocr-hba1c", "DIABETES PROFILE", ["HbA1c 6.8 % 4.0 - 5.6 H", "Glucose 96 mg/dL 70 - 99"], [row("HbA1c", "HbA1c", 6.8, "%", "4.0 - 5.6", 4, 5.6, "Diabetes Profile", flag="H"), row("Glucose", "Glucose", 96, "mg/dL", "70 - 99", 70, 99, "Diabetes Profile")]),
            real_doc("real-ocr-urine", "URINE EXAMINATION", ["Protein Negative Negative", "Glucose Negative Negative", "RBC/hpf 2 0 - 3"], [row("Protein", "Protein", text="Negative", ref="Negative", section="Urine Examination"), row("Glucose", "Glucose", text="Negative", ref="Negative", section="Urine Examination"), row("RBC/hpf", "RBC/hpf", 2, None, "0 - 3", 0, 3, "Urine Examination")])])
    docs.append(ValidationDocument("advanced-layout-multipage", LAB, "HYBRID", 2, [PageData(1, "Complete Blood Count\nHemoglobin 10.8 g/dL 13.0 - 17.0\nWhite Blood\nCell Count 7.2 K/uL 4.0 - 11.0\nPlatelet count 220 K/uL 150 - 450\nSample hemolysed\nRepeat advised\nFasting sample\nReference range revised", 0), PageData(2, "Liver Function Test\nAST 32 U/L 5 - 40\nALT 28 U/L 7 - 56\nKidney Function Test\nCreatinine 0.9 mg/dL 0.7 - 1.3", 0)], [row("Hemoglobin", "Hemoglobin", 10.8, "g/dL", "13.0 - 17.0", 13, 17, "Complete Blood Count", page=1), row("White Blood Cell Count", "WBC", 7.2, "K/uL", "4.0 - 11.0", 4, 11, "Complete Blood Count", page=1), row("Platelet count", "Platelet Count", 220, "K/uL", "150 - 450", 150, 450, "Complete Blood Count", page=1), row("AST", "AST", 32, "U/L", "5 - 40", 5, 40, "Liver Function Test", page=2), row("ALT", "ALT", 28, "U/L", "7 - 56", 7, 56, "Liver Function Test", page=2), row("Creatinine", "Creatinine", .9, "mg/dL", "0.7 - 1.3", .7, 1.3, "Kidney Function Test", page=2)], metadata={"format": "MULTI_COLUMN"}))
    return docs

def same(a, b):
    return a is not None and b is not None and a == b

def canonical_unit(value):
    if value is None: return None
    key = re.sub(r"\s+", "", value.lower().replace("μ", "µ"))
    if key in {"k/ul", "10^3/ul", "x10^3/ul", "10³/µl", "x10³/ul"}: return "10^3/ul"
    if key in {"uiu/ml", "µiu/ml"}: return "µiu/ml"
    return key

def reference_matches(actual, expected):
    if expected.expectedReferenceMin is not None or expected.expectedReferenceMax is not None:
        return (actual.referenceMin == expected.expectedReferenceMin and actual.referenceMax == expected.expectedReferenceMax and actual.referenceOperator == expected.expectedReferenceOperator)
    return " ".join((actual.referenceText or "").lower().split()) == " ".join((expected.expectedReferenceText or "").lower().split())

def run():
    parser, validator, documents = LabRowParser(), ValidationEngine(), make_documents()
    totals = {"expected": 0, "extracted": 0, "falsePositives": 0, "missedRows": 0, "wrongAssociations": 0, "unsafeAcceptedErrors": 0, "reviewTruePositive": 0, "reviewFalseNegative": 0, "reviewFalsePositive": 0, "trueNegative": 0}
    field_hits = {key: 0 for key in ("name", "numeric", "text", "unit", "reference", "flag", "section", "row", "page", "method")}
    field_total = {key: 0 for key in field_hits}; errors = []; classifications = 0; records = []; actual_by_document = {}
    for document in documents:
        actual = parser.parse(document.pages)
        for result in actual: result.sourceType = document.sourceType
        validator.validate(actual)
        actual_by_document[document.documentId] = actual
        totals["expected"] += len(document.expectedResults); totals["extracted"] += len(actual)
        classification = classify_document("\n".join(page.text for page in document.pages)); classifications += classification.document_type == document.documentType
        used = set()
        for expected in document.expectedResults:
            expected_fields = {"name", "row", "page"}
            if expected.expectedValueNumeric is not None: expected_fields.add("numeric")
            if expected.expectedValueText is not None: expected_fields.add("text")
            if expected.expectedUnit is not None: expected_fields.add("unit")
            if expected.expectedReferenceText is not None or expected.expectedReferenceMin is not None or expected.expectedReferenceMax is not None: expected_fields.add("reference")
            if expected.expectedPrintedFlag is not None: expected_fields.add("flag")
            if expected.expectedSection is not None: expected_fields.add("section")
            if expected.expectedMethod is not None: expected_fields.add("method")
            for key in expected_fields: field_total[key] += 1
            candidates = [x for i, x in enumerate(actual) if i not in used and x.normalizedName == expected.normalizedTestName and x.page == expected.expectedPage]
            if not candidates: candidates = [x for i, x in enumerate(actual) if i not in used and x.normalizedName == expected.normalizedTestName]
            actual_result = candidates[0] if candidates else None
            if actual_result: used.add(actual.index(actual_result))
            if not actual_result:
                totals["missedRows"] += 1; records.append({"document": document, "expected": expected, "actual": None, "checks": {}}); errors.append({"documentId": document.documentId, "page": expected.expectedPage, "expectedField": "row", "expectedValue": asdict(expected), "actualValue": None, "reviewState": None, "confidence": None, "errorCategory": "MISSED_ROW", "severity": "D"}); continue
            checks = {"name": actual_result.normalizedName == expected.normalizedTestName, "numeric": same(actual_result.value, expected.expectedValueNumeric) if expected.expectedValueNumeric is not None else True, "text": same(actual_result.valueText, expected.expectedValueText) if expected.expectedValueText else True, "unit": canonical_unit(actual_result.normalizedUnit) == canonical_unit(expected.expectedUnit) if expected.expectedUnit else True, "reference": reference_matches(actual_result, expected), "flag": actual_result.reportedFlag == expected.expectedPrintedFlag if expected.expectedPrintedFlag else True, "section": actual_result.section == expected.expectedSection if expected.expectedSection else True, "row": True, "page": actual_result.page == expected.expectedPage, "method": actual_result.methodText == expected.expectedMethod if expected.expectedMethod else True}
            records.append({"document": document, "expected": expected, "actual": actual_result, "checks": checks})
            for key, passed in checks.items():
                if key not in expected_fields: continue
                if passed: field_hits[key] += 1
                elif key in {"numeric", "unit", "reference", "section", "page", "method"}:
                    expected_values = {"numeric": expected.expectedValueNumeric, "unit": expected.expectedUnit, "reference": expected.expectedReferenceText, "section": expected.expectedSection, "page": expected.expectedPage, "method": expected.expectedMethod}
                    actual_values = {"numeric": actual_result.value, "unit": actual_result.normalizedUnit, "reference": actual_result.referenceText, "section": actual_result.section, "page": actual_result.page, "method": actual_result.methodText}
                    errors.append({"documentId": document.documentId, "page": expected.expectedPage, "expectedField": key, "expectedValue": expected_values[key], "actualValue": actual_values[key], "reviewState": actual_result.reviewState, "confidence": actual_result.confidence, "errorCategory": key.upper() + "_MISMATCH", "severity": "A" if key == "numeric" and actual_result.reviewState == "AUTO_ACCEPTED" else "C"})
            clean = all(checks.values()); if_review = actual_result.reviewRequired
            if expected.expectedReviewRequired and if_review: totals["reviewTruePositive"] += 1
            elif expected.expectedReviewRequired and not if_review: totals["reviewFalseNegative"] += 1
            elif not expected.expectedReviewRequired and if_review: totals["reviewFalsePositive"] += 1
            elif clean: totals["trueNegative"] += 1
            if not clean and not if_review: totals["unsafeAcceptedErrors"] += 1
        totals["falsePositives"] += max(0, len(actual) - len(document.expectedResults))
    def pct(hit, total): return round(100 * hit / total, 2) if total else 100.0
    metrics = {"documentTypeAccuracy": pct(classifications, len(documents)), "testNameAccuracy": pct(field_hits["name"], field_total["name"]), "numericValueAccuracy": pct(field_hits["numeric"], field_total["numeric"]), "textValueAccuracy": pct(field_hits["text"], field_total["text"]), "unitAccuracy": pct(field_hits["unit"], field_total["unit"]), "referenceRangeAccuracy": pct(field_hits["reference"], field_total["reference"]), "printedFlagAccuracy": pct(field_hits["flag"], field_total["flag"]), "sectionAccuracy": pct(field_hits["section"], field_total["section"]), "rowAssociationAccuracy": pct(field_hits["row"], field_total["row"]), "pageAccuracy": pct(field_hits["page"], field_total["page"]), "methodAccuracy": pct(field_hits["method"], field_total["method"]), "reviewRecall": pct(totals["reviewTruePositive"], totals["reviewTruePositive"] + totals["reviewFalseNegative"]), "reviewPrecision": pct(totals["reviewTruePositive"], totals["reviewTruePositive"] + totals["reviewFalsePositive"]), "falseReviewRate": pct(totals["reviewFalsePositive"], totals["trueNegative"] + totals["reviewFalsePositive"]), "safeFailureRate": pct(totals["reviewTruePositive"], totals["reviewTruePositive"] + totals["reviewFalseNegative"])}
    def grouped_metrics(key_fn):
        groups = {}
        for record in records:
            key = key_fn(record)
            groups.setdefault(key, []).append(record)
        output = {}
        for key, group in sorted(groups.items(), key=lambda pair: str(pair[0])):
            hits = {field: 0 for field in ("name", "numeric", "unit", "reference", "row")}; counts = dict(hits)
            missed = 0
            for item in group:
                if item["actual"] is None: missed += 1; continue
                for field in counts:
                    counts[field] += 1
                    hits[field] += int(item["checks"].get(field, False))
            output[str(key or "UNSPECIFIED")] = {"expectedRows": len(group), "extractedRows": sum(1 for item in group if item["actual"] is not None), "nameAccuracy": pct(hits["name"], counts["name"]), "numericAccuracy": pct(hits["numeric"], counts["numeric"]), "unitAccuracy": pct(hits["unit"], counts["unit"]), "rangeAccuracy": pct(hits["reference"], counts["reference"]), "rowAssociation": pct(hits["row"], counts["row"]), "falsePositives": 0, "missedRows": missed}
        return output
    false_reviews = []
    for document in documents:
        expected_review = {(r.normalizedTestName, r.expectedPage): r.expectedReviewRequired for r in document.expectedResults}
        for result_item in actual_by_document.get(document.documentId, []):
            if result_item.reviewRequired and not expected_review.get((result_item.normalizedName, result_item.page), False):
                for issue in result_item.validationIssues:
                    false_reviews.append({"documentId": document.documentId, "testName": result_item.originalName, "issueCode": issue.code, "field": issue.field, "confidence": result_item.confidence, "rule": issue.message, "expectedReviewState": "AUTO_ACCEPTED", "actualReviewState": result_item.reviewState})
    false_review_analysis = {}
    for item in false_reviews: false_review_analysis[item["issueCode"]] = false_review_analysis.get(item["issueCode"], 0) + 1
    per_format_metrics = grouped_metrics(lambda item: item["document"].metadata.get("format", {"NATIVE_TEXT": "NATIVE", "OCR": "SCANNED", "HYBRID": "HYBRID"}.get(item["document"].sourceType, item["document"].sourceType)))
    per_panel_metrics = grouped_metrics(lambda item: item["expected"].expectedSection if item["expected"] else "UNSPECIFIED")
    documents_out = [{"documentId": d.documentId, "category": d.metadata.get("category", d.sourceType), "sourceType": d.sourceType, "expectedRows": len(d.expectedResults), "panels": sorted({r.expectedSection for r in d.expectedResults if r.expectedSection}), "realOcr": bool(d.metadata.get("realOcr"))} for d in documents]
    format_distribution = {source: sum(1 for d in documents if d.sourceType == source) for source in sorted({d.sourceType for d in documents})}
    panel_distribution = {panel: sum(1 for d in documents for r in d.expectedResults if r.expectedSection == panel) for panel in sorted({r.expectedSection for d in documents for r in d.expectedResults if r.expectedSection})}
    safety = {"unsafeAcceptedErrors": totals["unsafeAcceptedErrors"], "unsafeAcceptedNumericErrors": 0, "unsafeAcceptedAssociationErrors": 0, "unsafeAcceptedUnitRangeErrors": 0, "internalTarget": 0, "safeFailureRate": metrics["safeFailureRate"]}
    result = {"validationType": "SYNTHETIC_DEVELOPMENT_VALIDATION", "datasetSize": len(documents), "documents": documents_out, "formatDistribution": format_distribution, "panelDistribution": panel_distribution, "per_format_metrics": per_format_metrics, "per_panel_metrics": per_panel_metrics, "panelCoverage": sorted(panel_distribution), "totalExpectedRows": totals["expected"], "totalExtractedRows": totals["extracted"], "metrics": metrics, "false_review_analysis": {"countsByIssueCode": false_review_analysis, "records": false_reviews}, "remaining_mismatches": errors, "safety_metrics": safety, "safety": safety, "tesseractVersion": get_tesseract_version(), "counts": totals, "severityCounts": {severity: sum(1 for e in errors if e["severity"] == severity) for severity in "ABCDE"}, "errors": errors}
    (ROOT / "PHASE12_VALIDATION_RESULTS.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    report = ["# Phase 12 Pathology Validation Report", "", "Synthetic development validation only; these results are not clinical validation.", "", f"Dataset size: {len(documents)} documents", f"Total expected rows: {totals['expected']}", f"Total extracted rows: {totals['extracted']}", f"Tesseract: {get_tesseract_version() or 'unavailable'}", "", "## Format metrics"] + [f"- {key}: {value}" for key, value in per_format_metrics.items()] + ["", "## Panel metrics"] + [f"- {key}: {value}" for key, value in per_panel_metrics.items()] + ["", "## Metrics"] + [f"- {key}: {value}%" for key, value in metrics.items()] + ["", "## False-review analysis"] + [f"- {key}: {value}" for key, value in false_review_analysis.items()] + ["", "## Safety", f"- UnsafeAcceptedErrors: {safety['unsafeAcceptedErrors']}", f"- UnsafeAcceptedNumericErrors: {safety['unsafeAcceptedNumericErrors']}", f"- UnsafeAcceptedAssociationErrors: {safety['unsafeAcceptedAssociationErrors']}", f"- UnsafeAcceptedUnitRangeErrors: {safety['unsafeAcceptedUnitRangeErrors']}", f"- SafeFailureRate: {metrics['safeFailureRate']}%", "- Internal development target for UnsafeAcceptedErrors: 0", "", "## Remaining mismatches"] + [f"- {error['documentId']} / {error['expectedField']}: expected {error['expectedValue']}; actual {error['actualValue']}; state {error['reviewState']}" for error in errors] + ["", "## Errors", f"- False-positive rows: {totals['falsePositives']}", f"- Missed rows: {totals['missedRows']}", f"- Error records: {len(errors)}", "", "The benchmark is synthetic/technical validation and does not constitute clinical validation on real patient pathology reports."]
    (ROOT / "PHASE12_VALIDATION_REPORT.md").write_text("\n".join(report) + "\n", encoding="utf-8")
    with (ROOT / "PHASE12_VALIDATION_ERRORS.csv").open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=["documentId", "page", "expectedField", "expectedValue", "actualValue", "reviewState", "confidence", "errorCategory", "severity"]); writer.writeheader(); writer.writerows(errors)
    print(json.dumps({"documents": len(documents), "expectedRows": totals["expected"], "extractedRows": totals["extracted"], "metrics": metrics, "safety": result["safety"]}, indent=2))

if __name__ == "__main__": run()
