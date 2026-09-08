from core.extractor import PageData
from parsers.lab_parser import LabRowParser
from normalization import TestNameNormalizer, UnitNormalizer
from reference_ranges import ReferenceRangeParser

def parse(text):
    return LabRowParser().parse([PageData(1, text, len(text))])

def test_standard_and_aliases():
    rows = parse("HAEMOGLOBIN (Hb) 10.8 gm/dl 13 - 17")
    assert rows[0].originalName == "HAEMOGLOBIN (Hb)"
    assert rows[0].normalizedName == "Hemoglobin"
    assert (rows[0].value, rows[0].originalUnit, rows[0].normalizedUnit) == (10.8, "gm/dl", "g/dL")

def test_multiline_table_and_sections():
    rows = parse("""Complete Blood Count
Hemoglobin
Result: 10.8 g/dL
Biological Reference Interval: 13.0 - 17.0
Creatinine 0.9 mg/dL 0.7 - 1.3""")
    assert [(x.normalizedName, x.value) for x in rows] == [("Hemoglobin", 10.8), ("Creatinine", .9)]
    assert rows[0].section == "Complete Blood Count"

def test_column_row_flag_and_textual_value():
    rows = parse("""Test Result Flag Unit Reference
CRP Negative N mg/L Negative
Hb 10.8 L gm/dl 13 - 17""")
    assert rows[0].value is None and rows[0].valueText == "Negative" and rows[0].referenceType == "TEXT_ONLY"
    assert rows[1].reportedFlag == "L"

def test_ocr_corruption_is_conservative():
    normalizer = TestNameNormalizer()
    assert normalizer.normalize("HemogIobin").normalized == "Hemoglobin"
    assert normalizer.normalize("Creatlnine").normalized == "Creatinine"
    assert normalizer.normalize("Unclear Marker").normalized == "Unclear Marker"

def test_reference_operators_and_missing_range():
    parser = ReferenceRangeParser()
    assert parser.parse("<=5.7").type == "LESS_THAN_OR_EQUAL"
    assert parser.parse(">40").type == "GREATER_THAN"
    assert parser.parse("13–17").minimum == 13
    assert parser.parse(None).minimum is None
    item = parse("HbA1c 6.8 %")[0]
    assert item.referenceType == "UNKNOWN" and item.referenceMin is None and item.referenceMax is None

def test_method_comments_and_duplicate_like_rows():
    rows = parse("""Method: Photometry
Hemoglobin 10.8 g/dL 13 - 17
Comment: repeat if clinically indicated
Hemoglobin 11.2 g/dL 13 - 17""")
    assert len(rows) == 2 and [x.value for x in rows] == [10.8, 11.2]
