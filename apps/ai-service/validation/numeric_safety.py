"""Technical persistence limits; never clinical reference ranges or corrections."""
from decimal import Decimal, InvalidOperation, localcontext
from .models import ValidationIssue

MAXIMUM = Decimal("99999999999999.9999")  # SQL decimal(18,4)
NUMERIC_CODES = {"NUMERIC_OUT_OF_RANGE", "NUMERIC_PRECISION_UNSUPPORTED"}


def isolate_unsafe_numbers(result):
    issues = [i for i in result.validationIssues if isinstance(i, ValidationIssue) and i.code in NUMERIC_CODES]
    raw_range = f"min={result.referenceMin}; max={result.referenceMax}"
    for attribute, field in (("value", "ValueNumeric"), ("referenceMin", "ReferenceMin"), ("referenceMax", "ReferenceMax")):
        candidate = getattr(result, attribute)
        if candidate is None:
            continue
        code = None
        try:
            number = Decimal(str(candidate))
            if not number.is_finite() or abs(number) > MAXIMUM:
                code = "NUMERIC_OUT_OF_RANGE"
            else:
                with localcontext() as context:
                    context.prec = 40
                    if number != number.quantize(Decimal("0.0001")):
                        code = "NUMERIC_PRECISION_UNSUPPORTED"
        except (InvalidOperation, ValueError):
            code = "NUMERIC_OUT_OF_RANGE"
        if code is None:
            continue
        if attribute == "value" and not result.valueText.strip():
            result.valueText = str(candidate)
        elif attribute != "value" and not result.referenceText:
            result.referenceText = raw_range
        setattr(result, attribute, None)
        issues.append(ValidationIssue(code=code, severity="HIGH", field=field,
            message="Extracted numeric candidate cannot be stored exactly in the supported decimal format. Verify the preserved source text."))
        if code not in (result.ambiguityReason or ""):
            result.ambiguityReason = "; ".join(filter(None, [result.ambiguityReason, code]))
    if issues:
        result.reviewRequired = True
        result.reviewState = "REVIEW_REQUIRED"
        result.calculatedStatus = "UNKNOWN"
    return issues
