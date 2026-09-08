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

    def parse(self, value: str | None) -> ReferenceRange:
        if not value or not value.strip():
            return ReferenceRange(text=None)
        text = " ".join(value.replace("–", "-").replace("—", "-").split())
        m = re.fullmatch(rf"({self.NUMBER})\s*(?:-|to)\s*({self.NUMBER})", text, re.I)
        if m:
            return ReferenceRange("BETWEEN", float(m.group(1)), float(m.group(2)), text=text)
        patterns = [("LESS_THAN_OR_EQUAL", r"(?:<=|≤)\s*({n})", "<="), ("LESS_THAN", r"<\s*({n})", "<"), ("GREATER_THAN_OR_EQUAL", r"(?:>=|≥)\s*({n})", ">="), ("GREATER_THAN", r">\s*({n})", ">")]
        for typ, pat, op in patterns:
            m = re.fullmatch(pat.format(n=self.NUMBER), text, re.I)
            if m:
                number = float(m.group(1))
                return ReferenceRange(typ, number if typ.startswith("GREATER") else None, number if typ.startswith("LESS") else None, op, text)
        for phrase, typ, op in [("up to", "LESS_THAN_OR_EQUAL", "<="), ("below", "LESS_THAN", "<"), ("less than", "LESS_THAN", "<"), ("above", "GREATER_THAN", ">"), ("greater than", "GREATER_THAN", ">")]:
            m = re.fullmatch(rf"{phrase}\s*({self.NUMBER})", text, re.I)
            if m:
                number = float(m.group(1))
                return ReferenceRange(typ, number if typ.startswith("GREATER") else None, number if typ.startswith("LESS") else None, op, text)
        return ReferenceRange("TEXT_ONLY", text=text)

