from dataclasses import dataclass
import re


@dataclass(frozen=True)
class ReferenceRange:
    type: str = "UNKNOWN"
    minimum: float | None = None
    maximum: float | None = None
    operator: str | None = None
    text: str | None = None


class ReferenceRangeParser:
    NUMBER = r"[+-]?\d+(?:\.\d+)?"

    def parse(self, value: str | None, *, age_years: float | None = None, sex: str | None = None) -> ReferenceRange:
        if not value or not value.strip():
            return ReferenceRange(text=None)
        original_text = " ".join(value.split())
        selected = self._select_demographic_range(original_text, age_years, sex)
        text = " ".join((selected or original_text).replace("–", "-").replace("—", "-").split())
        m = re.fullmatch(rf"({self.NUMBER})\s*(?:-|to)\s*({self.NUMBER})", text, re.I)
        if m:
            return ReferenceRange("BETWEEN", float(m.group(1)), float(m.group(2)), text=original_text)
        patterns = [("LESS_THAN_OR_EQUAL", r"(?:<=|≤)\s*({n})", "<="), ("LESS_THAN", r"<\s*({n})", "<"), ("GREATER_THAN_OR_EQUAL", r"(?:>=|≥)\s*({n})", ">="), ("GREATER_THAN", r">\s*({n})", ">")]
        for typ, pat, op in patterns:
            m = re.fullmatch(pat.format(n=self.NUMBER), text, re.I)
            if m:
                number = float(m.group(1))
                return ReferenceRange(typ, number if typ.startswith("GREATER") else None, number if typ.startswith("LESS") else None, op, original_text)
        for phrase, typ, op in [("up to", "LESS_THAN_OR_EQUAL", "<="), ("below", "LESS_THAN", "<"), ("less than", "LESS_THAN", "<"), ("above", "GREATER_THAN", ">"), ("greater than", "GREATER_THAN", ">")]:
            m = re.fullmatch(rf"{phrase}\s*({self.NUMBER})", text, re.I)
            if m:
                number = float(m.group(1))
                return ReferenceRange(typ, number if typ.startswith("GREATER") else None, number if typ.startswith("LESS") else None, op, original_text)
        return ReferenceRange("TEXT_ONLY", text=original_text)

    def _select_demographic_range(self, text: str, age_years: float | None, sex: str | None) -> str | None:
        """Select only an explicitly matching printed segment; otherwise preserve all text."""
        labels = list(re.finditer(r"(?P<label>male|female|men|women|adult|(?:\d+(?:\.\d+)?\s*(?:-|to)\s*\d+(?:\.\d+)?\s*(?:year|years|month|months)))\s*:\s*", text, re.I))
        if not labels:
            return None
        matches = []
        for index, match in enumerate(labels):
            label = match.group("label").lower()
            segment = text[match.end(): labels[index + 1].start() if index + 1 < len(labels) else len(text)].strip(" ;,")
            sex_match = sex and ((sex.lower().startswith("m") and label in {"male", "men"}) or (sex.lower().startswith("f") and label in {"female", "women"}))
            age_match = False
            age_range = re.fullmatch(r"(\d+(?:\.\d+)?)\s*(?:-|to)\s*(\d+(?:\.\d+)?)\s*(?:year|years|month|months)", label, re.I)
            if age_range and age_years is not None:
                low, high = float(age_range.group(1)), float(age_range.group(2))
                age_match = low <= age_years <= high
            if sex_match or age_match or (label == "adult" and age_years is not None and age_years >= 18):
                matches.append(segment)
        return matches[0] if len(matches) == 1 else None

