import json
import re
from typing import Iterable
from .models import ValidationConfig, ValidationIssue
from parsers.base import LabResultItem

class ValidationEngine:
    """Post-parser, deterministic extraction-quality validation."""
    LARGE_NATURAL = {"platelets", "wbc", "rbc", "vitamin b12", "ferritin", "triglycerides"}

    def __init__(self, config: ValidationConfig | None = None):
        self.config = config or ValidationConfig()

    def validate(self, results: list[LabResultItem]) -> list[list[ValidationIssue]]:
        output = [[] for _ in results]
        for i, result in enumerate(results):
            output[i].extend(self._validate_one(result))
        for i, result in enumerate(results):
            for j in range(i):
                if self._same_source(result, results[j]):
                    issue = ValidationIssue(code="DUPLICATE_SOURCE_CANDIDATE", severity="WARNING", field="BoundingBoxJson", message="Another result uses substantially the same source region.", requiresReview=True)
                    output[i].append(issue); output[j].append(issue.model_copy())
                if self._conflicting_duplicate(result, results[j]):
                    issue = ValidationIssue(code="CONFLICTING_DUPLICATE", severity="HIGH", field="ValueNumeric", message="Similar source rows produced conflicting extracted values.", requiresReview=True)
                    output[i].append(issue); output[j].append(issue.model_copy())
        for result, issues in zip(results, output):
            result.reviewRequired = result.reviewRequired or any(x.requiresReview for x in issues)
            result.reviewState = "REVIEW_REQUIRED" if result.reviewRequired else "AUTO_ACCEPTED"
            result.validationIssues = issues
        return output

    def _validate_one(self, result):
        issues = []
        fc = result.fieldConfidences or {}
        if not result.originalName.strip(): issues.append(self._issue("MISSING_TEST_NAME", "HIGH", "OriginalName", "Test name is missing."))
        if not result.valueText.strip(): issues.append(self._issue("MISSING_RESULT", "HIGH", "ValueText", "Result value is missing."))
        if result.page <= 0: issues.append(self._issue("MISSING_SOURCE_PAGE", "HIGH", "Page", "Source page is missing."))
        if not getattr(result, "sourceType", None): issues.append(self._issue("MISSING_SOURCE_TYPE", "HIGH", "SourceType", "Source type is missing."))
        if fc.get("value", result.confidence) < self.config.valueConfidence: issues.append(self._issue("LOW_VALUE_CONFIDENCE", "HIGH", "ValueNumeric", "Numeric or textual result confidence is below the review threshold."))
        if fc.get("ref", 1.0) < self.config.referenceConfidence: issues.append(self._issue("LOW_REFERENCE_CONFIDENCE", "HIGH", "ReferenceText", "Reference range confidence is below the review threshold."))
        if fc.get("name", result.confidence) < self.config.nameConfidence: issues.append(self._issue("LOW_NAME_CONFIDENCE", "HIGH", "OriginalName", "Test-name confidence is below the review threshold."))
        if fc.get("association", 1.0) < self.config.associationConfidence: issues.append(self._issue("ASSOCIATION_LOW_CONFIDENCE", "HIGH", "BoundingBoxJson", "Source association confidence is below the review threshold."))
        if result.referenceType == "BETWEEN" and (result.referenceMin is None or result.referenceMax is None or result.referenceMin > result.referenceMax):
            issues.append(self._issue("MALFORMED_REFERENCE_RANGE", "HIGH", "ReferenceText", "Numeric reference range is structurally malformed."))
        if result.referenceType in {"LESS_THAN","LESS_THAN_OR_EQUAL"} and result.referenceMax is None:
            issues.append(self._issue("MALFORMED_REFERENCE_RANGE", "HIGH", "ReferenceText", "Upper-bound reference operator has no threshold."))
        if result.referenceType in {"GREATER_THAN","GREATER_THAN_OR_EQUAL"} and result.referenceMin is None:
            issues.append(self._issue("MALFORMED_REFERENCE_RANGE", "HIGH", "ReferenceText", "Lower-bound reference operator has no threshold."))
        if result.discrepancy:
            issues.append(self._issue("REPORTED_FLAG_MISMATCH", "HIGH", "ReportedFlag", result.discrepancy))
        status = self._calculate_status(result)
        result.calculatedStatus = status
        expected = {"L": "LOW", "LOW": "LOW", "H": "HIGH", "HIGH": "HIGH", "N": "NORMAL", "NORMAL": "NORMAL"}.get((result.reportedFlag or "").upper())
        if expected and expected != status:
            issues.append(self._issue("REPORTED_FLAG_MISMATCH", "HIGH", "ReportedFlag", "Printed flag disagrees with deterministic extracted status; both values are preserved."))
        if result.value is not None and result.referenceMin is not None and result.referenceMax is not None:
            if result.value.is_integer() and result.normalizedName and result.normalizedName.lower() not in self.LARGE_NATURAL:
                candidate = result.value / 10
                if result.referenceMin * .5 <= candidate <= result.referenceMax * 1.5 and not result.referenceMin <= result.value <= result.referenceMax:
                    issues.append(self._issue("POSSIBLE_DECIMAL_ERROR", "WARNING", "ValueNumeric", "Extracted integer may represent a lost decimal point; review the source."))
            if result.value > max(abs(result.referenceMin), abs(result.referenceMax)) * 20 and result.normalizedName and result.normalizedName.lower() not in self.LARGE_NATURAL:
                issues.append(self._issue("VALUE_RANGE_STRUCTURAL_OUTLIER", "WARNING", "ValueNumeric", "Value is structurally far outside the extracted reference range."))
        if result.valueText and re.search(r"(?i)(?<![a-z])(o|i|l|s|b)(?=\d)|\d(?i:o|i|l|s|b)", result.valueText):
            issues.append(self._issue("OCR_CHARACTER_CONFUSION", "WARNING", "ValueText", "Source value contains a possible OCR character substitution."))
        if result.normalizedUnit and re.search(r"[^\w/%µμ^³. -]", result.normalizedUnit):
            issues.append(self._issue("UNIT_QUALITY_LOW", "WARNING", "Unit", "Unit contains unexpected characters; review the source."))
        if result.ambiguityReason:
            issues.append(self._issue("NORMALIZATION_AMBIGUOUS", "HIGH", "NormalizedName", result.ambiguityReason))
        return issues

    @staticmethod
    def _calculate_status(result):
        if result.value is None or result.referenceType in {"UNKNOWN", "TEXT_ONLY"}: return "UNKNOWN"
        if result.referenceType == "BETWEEN" and (result.referenceMin is None or result.referenceMax is None or result.referenceMin > result.referenceMax): return "UNKNOWN"
        if result.referenceType == "BETWEEN" and result.referenceMin is not None and result.referenceMax is not None:
            return "LOW" if result.value < result.referenceMin else "HIGH" if result.value > result.referenceMax else "NORMAL"
        if result.referenceType in {"LESS_THAN", "LESS_THAN_OR_EQUAL"} and result.referenceMax is not None:
            return "HIGH" if (result.value >= result.referenceMax and result.referenceType == "LESS_THAN") or result.value > result.referenceMax else "NORMAL"
        if result.referenceType in {"GREATER_THAN", "GREATER_THAN_OR_EQUAL"} and result.referenceMin is not None:
            return "LOW" if (result.value <= result.referenceMin and result.referenceType == "GREATER_THAN") or result.value < result.referenceMin else "NORMAL"
        return "UNKNOWN"
    def _same_source(self, a, b):
        if a.page != b.page or not a.boundingBoxJson or not b.boundingBoxJson: return False
        try:
            x,y=json.loads(a.boundingBoxJson),json.loads(b.boundingBoxJson)
            ix=max(0, min(x["x"]+x["width"],y["x"]+y["width"])-max(x["x"],y["x"]))
            iy=max(0, min(x["y"]+x["height"],y["y"]+y["height"])-max(x["y"],y["y"]))
            return ix*iy / max(1, min(x["width"]*x["height"],y["width"]*y["height"])) > .8
        except (KeyError, TypeError, ValueError, json.JSONDecodeError): return False

    def _conflicting_duplicate(self, a, b):
        return a.page == b.page and a.normalizedName and a.normalizedName == b.normalizedName and a.value != b.value

    @staticmethod
    def _issue(code, severity, field, message):
        return ValidationIssue(code=code, severity=severity, field=field, message=message)





