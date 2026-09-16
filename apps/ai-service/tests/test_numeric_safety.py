import pytest
from core.extractor import PageData
from parsers.base import LabResultItem
from parsers.lab_parser import LabRowParser
from validation import ValidationEngine


def row(**kwargs):
    return LabResultItem(originalName="Synthetic analyte", value=10.8, valueText="10.8", unit="g/dL",
        referenceType="BETWEEN", referenceText="13 - 17", referenceMin=13, referenceMax=17,
        sourceType="NATIVE_TEXT", confidence=.99).model_copy(update=kwargs)


@pytest.mark.parametrize("field", ["value", "referenceMin", "referenceMax"])
@pytest.mark.parametrize("value", [6392437665405192000., 100000000000000., -100000000000000., float('inf'), float('-inf'), float('nan')])
def test_extreme_candidate_is_preserved_as_text_and_isolated(field, value):
    bad = row(**{field: value, "valueText": "6392437665405192000.0000", "referenceText": "0 - 6392437665405192000.0000"})
    good = row()
    ValidationEngine().validate([bad, good])
    assert getattr(bad, field) is None
    assert bad.valueText == "6392437665405192000.0000"
    assert bad.referenceText == "0 - 6392437665405192000.0000"
    assert bad.reviewRequired and bad.reviewState == "REVIEW_REQUIRED"
    assert bad.calculatedStatus == "UNKNOWN"
    assert "NUMERIC_OUT_OF_RANGE" in bad.ambiguityReason
    assert any(i.code == "NUMERIC_OUT_OF_RANGE" for i in bad.validationIssues)
    assert good.value == 10.8 and not good.reviewRequired
    assert 'Infinity' not in bad.model_dump_json() and 'NaN' not in bad.model_dump_json()


def test_parser_to_validator_preserves_extreme_source_digits():
    results = LabRowParser().parse([PageData(page_number=1, character_count=100, text="Hemoglobin 6392437665405192000 g/dL 13 - 17\nCreatinine 0.9 mg/dL 0.7 - 1.3")])
    ValidationEngine().validate(results)
    assert len(results) == 2
    assert results[0].value is None
    assert results[0].valueText == "6392437665405192000"
    assert results[0].reviewRequired
    assert results[1].value == .9


def test_supported_magnitude_and_scale_remain_unchanged():
    result = row(value=99999999999999., valueText="99999999999999")
    ValidationEngine().validate([result])
    assert result.value == 99999999999999.
    assert not any(i.code.startswith('NUMERIC_') for i in result.validationIssues)
    precise = row(value=.00001, valueText="0.00001")
    ValidationEngine().validate([precise])
    assert precise.value is None and precise.valueText == "0.00001"
    assert "NUMERIC_PRECISION_UNSUPPORTED" in precise.ambiguityReason


def test_revalidation_keeps_numeric_review_evidence():
    result = row(value=1e20, valueText="100000000000000000000")
    validator = ValidationEngine()
    validator.validate([result]); validator.validate([result])
    assert any(i.code == 'NUMERIC_OUT_OF_RANGE' for i in result.validationIssues)
    assert result.reviewRequired and result.calculatedStatus == 'UNKNOWN'
