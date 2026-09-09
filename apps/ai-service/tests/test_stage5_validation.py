import json
from parsers.base import LabResultItem
from validation import ValidationEngine, ValidationConfig

def result(**kwargs):
    base = dict(originalName="Hemoglobin", normalizedName="Hemoglobin", value=10.8, valueText="10.8", unit="g/dL", referenceType="BETWEEN", referenceMin=13, referenceMax=17, page=1, sourceType="NATIVE_TEXT", confidence=.95, fieldConfidences={"name":.95,"value":.95,"unit":.95,"ref":.95,"association":.95})
    base.update(kwargs)
    return LabResultItem(**base)

def codes(item): return {issue.code for issue in item.validationIssues}

def test_low_critical_confidence_requires_review():
    item=result(fieldConfidences={"name":.98,"value":.55,"ref":.96})
    ValidationEngine().validate([item])
    assert item.reviewRequired and "LOW_VALUE_CONFIDENCE" in codes(item)

def test_missing_range_is_unknown_without_failure():
    item=result(referenceType="UNKNOWN", referenceMin=None, referenceMax=None)
    ValidationEngine().validate([item])
    assert item.calculatedStatus == "UNKNOWN" and not any(i.code == "MALFORMED_REFERENCE_RANGE" for i in item.validationIssues)

def test_malformed_range_requires_review():
    item=result(referenceType="BETWEEN", referenceMin=17, referenceMax=13)
    ValidationEngine().validate([item])
    assert "MALFORMED_REFERENCE_RANGE" in codes(item) and item.calculatedStatus == "UNKNOWN"

def test_flag_mismatch_preserves_both():
    item=result(value=6.8, valueText="6.8", referenceType="LESS_THAN", referenceMin=None, referenceMax=5.7, reportedFlag="NORMAL")
    ValidationEngine().validate([item])
    assert item.calculatedStatus == "HIGH" and "REPORTED_FLAG_MISMATCH" in codes(item)

def test_decimal_and_character_anomalies_do_not_correct_value():
    item=result(value=108, valueText="1O8")
    ValidationEngine().validate([item])
    assert item.value == 108 and {"POSSIBLE_DECIMAL_ERROR","OCR_CHARACTER_CONFUSION"} <= codes(item)

def test_duplicates_and_ambiguity():
    box=json.dumps({"x":1,"y":1,"width":100,"height":30,"page":1})
    a=result(boundingBoxJson=box)
    b=result(value=108, valueText="108", boundingBoxJson=box, ambiguityReason="test name matched multiple aliases")
    ValidationEngine().validate([a,b])
    assert "DUPLICATE_SOURCE_CANDIDATE" in codes(a) and "NORMALIZATION_AMBIGUOUS" in codes(b) and "CONFLICTING_DUPLICATE" in codes(b)

def test_textual_result_and_verification_state():
    item=result(value=None, valueText="Negative", referenceType="TEXT_ONLY", referenceMin=None, referenceMax=None)
    ValidationEngine().validate([item])
    assert item.calculatedStatus == "UNKNOWN" and not item.validationIssues
    item.reviewRequired=False
    item.reviewState="HUMAN_VERIFIED"
    assert item.confidence == .95 and item.reviewState == "HUMAN_VERIFIED"

def test_thresholds_are_configurable():
    item=result(fieldConfidences={"name":.75,"value":.75,"ref":.75})
    ValidationEngine(ValidationConfig(valueConfidence=.7, nameConfidence=.7, referenceConfidence=.7)).validate([item])
    assert not item.reviewRequired
