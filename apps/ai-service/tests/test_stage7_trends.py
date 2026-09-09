from datetime import date
from decimal import Decimal
from trends import TrendRequest, build_trend, summarize_trend

def point(report, name, value, unit="%", day="2026-01-10", **kw):
    return {"reportId": report, "test": name, "value": value, "valueText": str(value), "unit": unit, "date": day, "status": "HIGH", **kw}

def test_aliases_and_percentage_points():
    trend = build_trend(TrendRequest(test="Hemoglobin", results=[point("1", "Hb", 10.8, "g/dL"), point("2", "HGB", 11.2, "g/dL", "2026-04-10"), point("3", "Haemoglobin", 12, "g/dL", "2026-09-10")]))
    assert trend["test"] == "hemoglobin" and len(trend["points"]) == 3 and trend["absoluteChange"] == Decimal("1.2")

def test_percentage_point_change_and_neutral_direction():
    trend = build_trend(TrendRequest(test="HbA1c", results=[point("1", "HbA1c", 7.4), point("2", "HbA1c", 7.0, day="2026-04-10"), point("3", "HbA1c", 6.8, day="2026-09-01")]))
    assert trend["percentagePointChange"] == Decimal("-0.6") and trend["percentChange"] is None and trend["direction"] == "DECREASED"
    assert "improv" not in summarize_trend(trend).lower()

def test_review_missing_date_and_text_are_excluded():
    trend = build_trend(TrendRequest(test="HbA1c", results=[point("1", "HbA1c", 7.4), point("2", "HbA1c", 68, day="2026-04-10", reviewState="REVIEW_REQUIRED", reviewRequired=True), point("3", "HbA1c", None, day=None, valueText="Pending")]))
    assert len(trend["requiresVerificationTrendPoints"]) == 1 and "DATE_MISSING" in trend["validationIssues"]

def test_unit_mismatch_and_duplicate_are_excluded():
    trend = build_trend(TrendRequest(test="Creatinine", results=[point("1", "Creatinine", .9, "mg/dL"), point("2", "Creatinine", 80, "umol/L", "2026-02-10")]))
    assert trend["points"][0]["included"] is False and "UNIT_MISMATCH" in trend["points"][0]["exclusionReason"]
    dup = build_trend(TrendRequest(test="Creatinine", results=[point("1", "Creatinine", .9, "mg/dL"), point("2", "Creatinine", .9, "mg/dL")]))
    assert all(p["included"] is False for p in dup["points"])
