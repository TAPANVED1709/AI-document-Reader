from datetime import date
from decimal import Decimal
from pydantic import BaseModel, Field


class TrendResult(BaseModel):
    reportId: str
    date: date | None
    dateSource: str = "UNKNOWN"
    test: str
    value: Decimal | None = None
    valueText: str = ""
    unit: str | None = None
    originalUnit: str | None = None
    reference: str | None = None
    referenceMin: Decimal | None = None
    referenceMax: Decimal | None = None
    status: str = "UNKNOWN"
    reviewState: str = "AUTO_ACCEPTED"
    included: bool = True
    exclusionReason: str | None = None


class TrendRequest(BaseModel):
    results: list[TrendResult] = Field(default_factory=list)
    test: str | None = None
    fromDate: date | None = None
    toDate: date | None = None


def _unit(value: str | None) -> str:
    return (value or "").strip().lower()


def _canonical(name: str) -> str:
    key = "".join(ch for ch in name.lower() if ch.isalnum())
    return {"hb": "hemoglobin", "hgb": "hemoglobin", "haemoglobin": "hemoglobin", "glycosylatedhemoglobin": "hba1c"}.get(key, key)


def build_trend(request: TrendRequest) -> dict:
    selected = [_ for _ in request.results if (not request.test or _canonical(_.test) == _canonical(request.test)) and (not request.fromDate or (_.date and _.date >= request.fromDate)) and (not request.toDate or (_.date and _.date <= request.toDate))]
    selected.sort(key=lambda r: (r.date is None, r.date or date.max, r.reportId))
    canonical = _canonical(request.test or (selected[0].test if selected else ""))
    included = [r for r in selected if r.included and r.date is not None and r.value is not None and r.reviewState != "REVIEW_REQUIRED"]
    excluded = [r for r in selected if r not in included]
    units = {_unit(r.unit) for r in included if _unit(r.unit)}
    if len(units) > 1:
        for r in included:
            r.included = False; r.exclusionReason = "UNIT_MISMATCH"
        excluded.extend(included); included = []
    duplicate_keys = {(r.date, _unit(r.unit), r.value) for r in included}
    for key in duplicate_keys:
        same = [r for r in included if (r.date, _unit(r.unit), r.value) == key]
        if len(same) > 1:
            for r in same: r.included = False; r.exclusionReason = "DUPLICATE_DATE_VALUE"
            excluded.extend(same)
        
    included = [r for r in included if r.included]
    values = [r.value for r in included]
    unit = next((r.unit for r in included if r.unit), None)
    first, latest = (values[0], values[-1]) if values else (None, None)
    absolute = latest - first if first is not None and latest is not None else None
    is_percent = _unit(unit) in {"%", "percent", "percentage"}
    result = {
        "test": canonical or (selected[0].test if selected else ""), "unit": unit,
        "points": [r.model_dump(mode="json") for r in selected],
        "requiresVerificationTrendPoints": [r.model_dump(mode="json") for r in excluded if r.reviewState == "REVIEW_REQUIRED"],
        "validationIssues": (["DATE_MISSING"] if any(r.date is None for r in selected) else []) + (["INSUFFICIENT_POINTS"] if len(included) < 2 else []),
        "firstValue": first, "latestValue": latest, "absoluteChange": absolute,
        "percentagePointChange": absolute if is_percent else None,
        "percentChange": (absolute / first * 100 if absolute is not None and first not in (None, 0) and not is_percent else None),
        "direction": "INCREASED" if absolute is not None and absolute > 0 else "DECREASED" if absolute is not None and absolute < 0 else "UNCHANGED" if absolute is not None else "INSUFFICIENT_DATA",
    }
    return result


def summarize_trend(trend: dict) -> str:
    points = [p for p in trend["points"] if p.get("included", True) and p.get("value") is not None and p.get("date")]
    if len(points) < 2: return f"{len(points)} validated {trend['test']} result is available; at least two dated numeric results are needed for a comparison."
    change = trend.get("percentagePointChange") if trend.get("percentagePointChange") is not None else trend.get("absoluteChange")
    suffix = " percentage points" if trend.get("percentagePointChange") is not None else ""
    return f"{len(points)} validated {trend['test']} results are available. The value changed from {points[0]['value']} to {points[-1]['value']}, a {trend['direction'].lower()} of {abs(change)}{suffix}."
