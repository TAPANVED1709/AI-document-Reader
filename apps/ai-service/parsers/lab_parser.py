"""
Modular Laboratory Report Row Parser (Version 1).
Extracts laboratory test rows conservatively from plain-text PDF extractions.
"""
import re
import json
from typing import List, Optional, Tuple
from core.extractor import PageData
from .base import BaseParser, LabResultItem


class LabRowParser(BaseParser):
    """
    Conservative regex and token-based parser for pathology/laboratory test rows.
    Identifies rows matching:
    [Test Name] [Numeric/Text Result] [Unit] [Reference Range]
    """

    # Common units used in pathology and laboratory medicine
    UNITS_PATTERN = (
        r"(?:g/dL|mg/dL|ug/dL|mcg/dL|pg/mL|ng/mL|ug/mL|mcg/mL|ng/dL|"
        r"x10[³3\^]*/[uμ]L|10[³3\^]*/[uμ]L|x10[⁶6\^]*/[uμ]L|10[⁶6\^]*/[uμ]L|10\^9/L|"
        r"cells/[uμ]L|cells/mcL|[uμ]L|mcL|fL|fl|pg|"
        r"mmol/L|umol/L|pmol/L|nmol/L|mEq/L|IU/L|U/L|[uμ]IU/mL|mIU/L|"
        r"%|mg/L|ug/L|sec|seconds|mm/hr|mL/min|ratio)"
    )

    # Common test name normalization map
    NORMALIZATION_MAP = {
        "hb": "Hemoglobin",
        "hgb": "Hemoglobin",
        "hemoglobin": "Hemoglobin",
        "wbc": "WBC",
        "white blood cell count": "WBC",
        "white blood cells": "WBC",
        "rbc": "RBC",
        "red blood cell count": "RBC",
        "red blood cells": "RBC",
        "platelet count": "Platelets",
        "platelets": "Platelets",
        "plt": "Platelets",
        "hba1c": "HbA1c",
        "glycated hemoglobin": "HbA1c",
        "vitamin b12": "Vitamin B12",
        "vit b12": "Vitamin B12",
        "total cholesterol": "Total Cholesterol",
        "cholesterol, total": "Total Cholesterol",
        "hdl cholesterol": "HDL Cholesterol",
        "ldl cholesterol": "LDL Cholesterol",
        "triglycerides": "Triglycerides",
        "fasting blood sugar": "Glucose, Fasting",
        "glucose, fasting": "Glucose, Fasting",
        "creatinine": "Creatinine",
        "serum creatinine": "Creatinine",
        "blood urea nitrogen": "BUN",
        "bun": "BUN",
        "tsh": "TSH",
        "potassium": "Potassium",
        "sodium": "Sodium",
        "calcium": "Calcium",
        "sgpt": "SGPT (ALT)",
        "alt": "SGPT (ALT)",
        "sgot": "SGOT (AST)",
        "ast": "SGOT (AST)",
    }

    # Header patterns to skip
    HEADER_KEYWORDS = [
        "test name", "investigation", "parameter", "result", "reference range",
        "reference interval", "biological reference", "units", "unit",
        "patient name", "report date", "sample date", "collection date",
        "age / sex", "age/sex", "referred by", "dr.", "doctor",
        "page", "laboratory report", "pathology report"
    ]

    def __init__(self):
        # Compiled patterns for parsing
        # Pattern 1: Standard row: Name + Result + Unit + Range
        # Example: Hemoglobin     10.8     g/dL      13.0 - 17.0
        self.row_pattern_with_unit = re.compile(
            r"^(?P<name>[A-Za-z0-9\s,\-\(\)\/\.]+?)\s+"
            r"(?P<value>(?:[<>]=?|[≤≥])?\s*[+-]?\d+(?:\.\d+)?)\s+"
            r"(?P<unit>" + self.UNITS_PATTERN + r")\s+"
            r"(?P<ref>(?:(?:<=|>=|<|>|≤|≥|Up to|Less than|More than)\s*[+-]?\d+(?:\.\d+)?|[+-]?\d+(?:\.\d+)?\s*(?:-|–|—|to)\s*[+-]?\d+(?:\.\d+)?))",
            re.IGNORECASE
        )

        # Pattern 2: Row where unit might be omitted or after range
        # Example: HbA1c 6.8 4.0 - 5.6 % or Calcium 9.2 8.5 - 10.2
        self.row_pattern_no_unit = re.compile(
            r"^(?P<name>[A-Za-z0-9\s,\-\(\)\/\.]+?)\s+"
            r"(?P<value>(?:[<>]=?|[≤≥])?\s*[+-]?\d+(?:\.\d+)?)\s+"
            r"(?P<ref>(?:(?:<=|>=|<|>|≤|≥|Up to|Less than|More than)\s*[+-]?\d+(?:\.\d+)?|[+-]?\d+(?:\.\d+)?\s*(?:-|–|—|to)\s*[+-]?\d+(?:\.\d+)?))"
            r"(?:\s+(?P<unit>" + self.UNITS_PATTERN + r"))?",
            re.IGNORECASE
        )

        # Range extraction sub-patterns
        self.interval_range_pattern = re.compile(
            r"^([+-]?\d+(?:\.\d+)?)\s*(?:-|–|—|to)\s*([+-]?\d+(?:\.\d+)?)$",
            re.IGNORECASE
        )
        self.interval_upper_pattern = re.compile(
            r"^(?:<=|<|≤|Less than|Up to)\s*([+-]?\d+(?:\.\d+)?)$",
            re.IGNORECASE
        )
        self.interval_lower_pattern = re.compile(
            r"^(?:>=|>|≥|Greater than|More than)\s*([+-]?\d+(?:\.\d+)?)$",
            re.IGNORECASE
        )

    def parse(self, pages: List[PageData]) -> List[LabResultItem]:
        """Extract lab results across all pages."""
        results: List[LabResultItem] = []

        for page in pages:
            token_lines = {}
            for token in page.tokens:
                token_lines.setdefault(token.line, []).append(token)
            rows = [sorted(token_lines[key], key=lambda t: t.x) for key in sorted(token_lines)]
            lines = [" ".join(t.text for t in row) for row in rows] if rows else page.text.splitlines()
            for index, line in enumerate(lines):
                cleaned_line = line.strip()
                if not cleaned_line:
                    continue

                # Skip header / demographic lines
                if self._is_header_or_metadata(cleaned_line):
                    continue

                item = self._parse_line(cleaned_line, page.page_number)
                if item:
                    if page.source == "OCR":
                        self._apply_ocr_metadata(item, cleaned_line, rows[index] if rows else [])
                    results.append(item)

        return results

    def _apply_ocr_metadata(self, item, line, tokens):
        match = self.row_pattern_with_unit.match(line) or self.row_pattern_no_unit.match(line)
        spans = []
        offset = 0
        for token in tokens:
            spans.append((offset, offset + len(token.text), token))
            offset += len(token.text) + 1
        used = []
        for group in ("name", "value", "unit", "ref"):
            start, end = match.span(group)
            selected = [t for a, b, t in spans if a < end and b > start]
            if start >= 0:
                item.fieldConfidences[group] = min((t.confidence for t in selected), default=0.)
                used.extend(selected)
        item.confidence = min(item.confidence, min(item.fieldConfidences.values(), default=0.))
        item.lowConfidence = item.confidence < 0.8
        if tokens:
            # The source row is the most useful review target. Include every
            # OCR token on the reconstructed line, including separators or
            # symbols that were not part of a regex capture.
            row_tokens = tokens
            x, y = min(t.x for t in row_tokens), min(t.y for t in row_tokens)
            item.boundingBoxJson = json.dumps({
                "x": x, "y": y, "width": max(t.x + t.width for t in row_tokens) - x,
                "height": max(t.y + t.height for t in row_tokens) - y,
                "page": item.page, "coordinateSpace": "rendered_pixels", "dpi": 300,
            })

    def _is_header_or_metadata(self, line: str) -> bool:
        """Check if line is a table header or patient metadata row."""
        line_lower = line.lower()
        # If line contains 2 or more header keywords, it is very likely a header
        header_matches = sum(1 for kw in self.HEADER_KEYWORDS if kw in line_lower)
        if header_matches >= 2:
            return True
        # Exact short headers
        if line_lower in ["test", "investigation", "complete blood count", "cbc", "lipid profile", "renal function test"]:
            return True
        return False

    def _parse_line(self, line: str, page_number: int) -> Optional[LabResultItem]:
        """Attempt to match a line against lab row patterns."""
        # Try pattern with unit first
        match = self.row_pattern_with_unit.match(line)
        confidence = 0.95

        if not match:
            # Fall back to pattern without unit / trailing unit
            match = self.row_pattern_no_unit.match(line)
            confidence = 0.85

        if not match:
            return None

        raw_name = match.group("name").strip()
        raw_value = match.group("value").strip()
        raw_ref = match.group("ref").strip()
        raw_unit = match.group("unit").strip() if match.group("unit") else None

        # Name sanity check: Test name should not be purely digits and should have reasonable length
        if not raw_name or len(raw_name) < 2 or re.match(r"^[\d\W]+$", raw_name):
            return None

        # Parse numeric value
        numeric_val = self._parse_numeric_value(raw_value)

        # Parse reference range bounds
        ref_min, ref_max = self._parse_reference_bounds(raw_ref)

        # Normalized test name
        norm_name = self._normalize_test_name(raw_name)

        # Adjust confidence
        if numeric_val is None:
            confidence -= 0.15
        if not raw_unit:
            confidence -= 0.10
        if ref_min is None and ref_max is None:
            confidence -= 0.20

        confidence = max(0.50, min(0.98, confidence))

        return LabResultItem(
            originalName=raw_name,
            normalizedName=norm_name,
            value=numeric_val,
            valueText=raw_value,
            unit=raw_unit,
            referenceMin=ref_min,
            referenceMax=ref_max,
            referenceText=raw_ref,
            page=page_number,
            confidence=round(confidence, 2)
        )

    def _parse_numeric_value(self, val_str: str) -> Optional[float]:
        """Convert string result to float, handling inequalities."""
        cleaned = re.sub(r"[<>=≤≥]", "", val_str).strip()
        try:
            return float(cleaned)
        except ValueError:
            return None

    def _parse_reference_bounds(self, ref_str: str) -> Tuple[Optional[float], Optional[float]]:
        """Extract minimum and maximum bounds from reference range string."""
        cleaned = ref_str.strip()

        # Check standard interval "min - max"
        m_range = self.interval_range_pattern.match(cleaned)
        if m_range:
            try:
                min_val = float(m_range.group(1))
                max_val = float(m_range.group(2))
                return min_val, max_val
            except ValueError:
                pass

        # Check upper bound "< max"
        m_upper = self.interval_upper_pattern.match(cleaned)
        if m_upper:
            try:
                max_val = float(m_upper.group(1))
                return None, max_val
            except ValueError:
                pass

        # Check lower bound "> min"
        m_lower = self.interval_lower_pattern.match(cleaned)
        if m_lower:
            try:
                min_val = float(m_lower.group(1))
                return min_val, None
            except ValueError:
                pass

        return None, None

    def _normalize_test_name(self, raw_name: str) -> str:
        """Map raw test name to standard canonical name where known."""
        cleaned = " ".join(raw_name.split()).lower()
        if cleaned in self.NORMALIZATION_MAP:
            return self.NORMALIZATION_MAP[cleaned]
        # Return cleaned raw name if no mapping exists
        return " ".join(raw_name.split())
