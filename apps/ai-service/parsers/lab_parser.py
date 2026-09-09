"""Deterministic, conservative multi-layout laboratory parser."""
import json
import re
from statistics import median
from typing import List, Optional
from core.extractor import PageData
from normalization import TestNameNormalizer, UnitNormalizer
from reference_ranges import ReferenceRangeParser
from .base import BaseParser, LabResultItem

class LabRowParser(BaseParser):
    VALUE = r"(?<![A-Za-z0-9])(?:[<>]=?|[≤≥])?\s*[+-]?(?:\d+(?:\.\d+)?|\.\d+)|(?:Non-?Reactive|Reactive|Negative|Positive|Trace|Nil|Absent|Present)"
    UNIT = r"(?:gm?\s*/\s*d[Ll]|g\s*/\s*d[Ll]|mg\s*/\s*d[Ll]|mcg\s*/\s*d[Ll]|ug\s*/\s*d[Ll]|mmol\s*/\s*[Ll]|mEq\s*/\s*[Ll]|meq\s*/\s*[Ll]|IU\s*/\s*[Ll]|U\s*/\s*[Ll]|[µu]IU\s*/\s*m[Ll]|mIU\s*/\s*[Ll]|(?:x\s*)?10[³3\^�]*\s*/\s*[kKµu]L|[kK]\s*/\s*[uµ]L|pg(?:\s*/\s*m[Ll])?|ng\s*/\s*[mMdD][Ll]|mm\s*/\s*hr|sec(?:onds?)?|[µu]g\s*/\s*d[Ll]|%|mg\s*/\s*[Ll]|ug\s*/\s*[Ll]|f[Ll]|m[Ll]/min|ratio)"
    FLAGS = {"h":"H","high":"HIGH","l":"L","low":"LOW","n":"N","normal":"NORMAL","a":"A","abnormal":"ABNORMAL","*":"*"}
    SECTIONS = {"complete blood count":"Complete Blood Count","cbc":"Complete Blood Count","hematology":"Hematology","liver function test":"Liver Function Test","lft":"Liver Function Test","kidney function test":"Kidney Function Test","kidney function":"Kidney Function Test","kft":"Kidney Function Test","renal function test":"Renal Function Test","rft":"Renal Function Test","lipid profile":"Lipid Profile","thyroid profile":"Thyroid Profile","diabetes profile":"Diabetes Profile","glucose":"Diabetes Profile","iron studies":"Iron Studies","vitamin profile":"Vitamin Profile","coagulation":"Coagulation","inflammation":"Inflammation","urine examination":"Urine Examination","urine routine":"Urine Examination","serology":"Serology","immunology":"Immunology","electrolytes":"Electrolytes"}
    IGNORE = ("patient","report","laboratory","pathology","address","barcode","certified","interpretation","comment","note:","doctor","referred","collection date","sample date")

    def __init__(self):
        self.names = TestNameNormalizer()
        self.units = UnitNormalizer()
        self.references = ReferenceRangeParser()
        self.SECTIONS = {**self.SECTIONS, "biochemistry": "Biochemistry", "metabolic": "Metabolic"}

    def parse(self, pages: List[PageData]) -> List[LabResultItem]:
        results = []
        for page in pages:
            rows = self._rows(page)
            section = None
            section_confidence = 1.0
            age_years, sex = self._metadata(page.text)
            method = None
            section_has_result = False
            consumed = set()
            for index, (line, tokens) in enumerate(rows):
                if index in consumed: continue
                key = self._key(line)
                if page.source == "OCR" and key == "lron studies": key = "iron studies"
                if key in self.SECTIONS:
                    section = self.SECTIONS[key]
                    section_confidence = min((t.confidence for t in tokens),default=1.0)
                    method = None; section_has_result = False; continue
                row_method_match = re.match(r"method\s*:\s*(.+)", line, re.I)
                if row_method_match:
                    if not section_has_result: method = row_method_match.group(1).strip()
                    continue
                if self._ignored(line): continue
                # Join a wrapped name only when the combined name is recognized.
                # Never greedily absorb the next three complete result rows.
                if self._looks_like_name(line) and index + 1 < len(rows):
                    following, following_tokens = rows[index + 1]
                    if self._key(following) not in self.SECTIONS:
                        candidate = self._parse_row(line + " " + following, page.page_number, section, tokens + following_tokens, method, age_years, sex)
                        if candidate and self.names.normalize(candidate.originalName).confidence >= .88:
                            line += " " + following; tokens = tokens + following_tokens; consumed.add(index + 1)
                # A dangling printed interval may continue on one numeric-only line.
                next_index = index + 2 if index + 1 in consumed else index + 1
                if re.search(r"\d\s*[-–]\s*$", line) and next_index < len(rows):
                    following, following_tokens = rows[next_index]
                    if re.fullmatch(r"[+-]?(?:\d+(?:\.\d+)?|\.\d+)", following.strip()):
                        line += " " + following; tokens = tokens + following_tokens; consumed.add(next_index)
                item = self._parse_row(line, page.page_number, section, tokens, method, age_years, sex)
                if item:
                    if section is not None:
                        item.fieldConfidences["association"] = section_confidence
                    item.confidence = min(item.confidence, section_confidence)
                    item.lowConfidence |= section_confidence < .8
                    item.reviewRequired |= section_confidence < .8
                    results.append(item); section_has_result = True
        return results

    def _rows(self, page):
        if page.tokens:
            # OCR line IDs sometimes span two panels or split one printed row.
            # Recover physical baselines, retaining every source token/confidence.
            bands = []
            typical_height = median(t.height for t in page.tokens if t.height > 0)
            for token in sorted(page.tokens, key=lambda t: (t.y + t.height / 2, t.x)):
                center = token.y + token.height / 2
                band = next((b for b in reversed(bands) if abs(b[0] - center) < typical_height * .65), None)
                if band is None: band = [center, []]; bands.append(band)
                band[1].append(token)
            cuts = []
            for _, band in bands:
                ordered = sorted(band, key=lambda t:t.x)
                for i in range(1,len(ordered)):
                    left,right=ordered[i-1],ordered[i]
                    if right.x-left.x-left.width < typical_height*6: continue
                    text=" ".join(t.text for t in ordered[i:])
                    # A numeric/unit/reference cell is not a separate panel.
                    is_heading=self._key(text) in self.SECTIONS
                    is_row=bool(re.match(r"[A-Za-z]",text) and re.search(self.VALUE,text,re.I) and not self._ignored(text))
                    if is_heading or is_row: cuts.append((left.x+left.width+right.x)/2)
            # Require repeated evidence for a gutter, not a single wide space.
            gutter=median(cuts) if len(cuts)>=2 else None
            output=[]
            for column in range(2 if gutter is not None else 1):
                column_tokens=[t for t in page.tokens if gutter is None or (t.x>=gutter)==bool(column)]
                column_bands=[]
                for token in sorted(column_tokens,key=lambda t:(t.y+t.height/2,t.x)):
                    center=token.y+token.height/2
                    band=next((b for b in reversed(column_bands) if abs(b[0]-center)<typical_height*.65),None)
                    if band is None: band=[center,[]]; column_bands.append(band)
                    band[1].append(token)
                for _,band in column_bands:
                    selected=sorted(band,key=lambda t:t.x)
                    output.append((" ".join(t.text for t in selected),selected))
            return output
        return [(line.strip(), []) for line in page.text.splitlines() if line.strip()]

    @staticmethod
    def _key(value): return re.sub(r"[^a-z0-9]+", " ", value.lower()).strip()

    def _ignored(self, line):
        low = line.lower().strip()
        if re.fullmatch(r"page\s+\d+\s*(?:(?:of|/)\s*\d+)?", low): return True
        if low.startswith(("sample hemolysed", "sample hemolyzed", "repeat advised", "fasting sample", "reference range revised")): return True
        if any(x in low for x in self.IGNORE): return True
        if low.startswith("cell count "): return True
        if low.startswith(("method:", "reference range", "biological reference", "observed value", "test name", "investigation", "parameter", "age:", "age ", "sex:", "sex ", "gender:", "gender ")): return True
        return sum(x in low for x in ("result","unit","reference","flag")) >= 2

    def _looks_like_name(self, line):
        return bool(re.search(r"[A-Za-z]{2,}", line)) and not re.search(self.VALUE, line, re.I)

    def _parse_row(self, line: str, page: int, section: str | None, tokens, method: str | None = None, age_years: float | None = None, sex: str | None = None) -> Optional[LabResultItem]:
        text = " ".join(line.replace("：", ":").split())
        explicit_method = re.search(r"\bmethod\s*:\s*(.+)$", text, re.I)
        if explicit_method:
            method = explicit_method.group(1).strip()
            text = text[:explicit_method.start()].strip()
        text = re.sub(r"\b(?:result|observed value|value)\s*:\s*", "", text, flags=re.I)
        values = list(re.finditer(self.VALUE, text, re.I))
        if not values: return None
        first = values[0]
        raw_name = text[:first.start()].strip(" :-|\t")
        if not raw_name or len(raw_name) < 2 or not re.search("[A-Za-z]", raw_name) or self._ignored(raw_name): return None
        value_text = first.group(0).strip()
        remainder = text[first.end():].strip(" :|\t")
        unit_match = re.match(self.UNIT, remainder, re.I)
        original_unit = unit_match.group(0) if unit_match else None
        after_unit = remainder[unit_match.end():].strip(" :|\t") if unit_match else remainder
        after_unit = re.sub(r"\b(?:reference(?: range| interval)?|biological reference interval|reference)\s*:?\s*", "", after_unit, flags=re.I).strip()
        flag = None
        flag_match = re.match(r"(H|HIGH|L|LOW|N|NORMAL|A|ABNORMAL|\*)\b", after_unit, re.I)
        if flag_match:
            flag = self.FLAGS[flag_match.group(1).lower()]; after_unit = after_unit[flag_match.end():].strip()
        trailing_flag = re.search(r"(?:^|\s)(H|HIGH|L|LOW|N|NORMAL|A|ABNORMAL|\*)$", after_unit, re.I)
        if trailing_flag:
            flag = self.FLAGS[trailing_flag.group(1).lower()]
            after_unit = after_unit[:trailing_flag.start()].strip()
        ref = self.references.parse(after_unit if after_unit else None, age_years=age_years, sex=sex)
        numeric = self._number(value_text)
        operator = re.match(r"([<>]=?|[≤≥])", value_text)
        name = self.names.normalize(raw_name)
        textual = value_text.lower() in {"negative","positive","reactive","non-reactive","trace","nil","absent","present"}
        demographic_ambiguous = ref.type == "TEXT_ONLY" and bool(re.search(r"\b(?:male|female|adult|year|years|month|months)\s*:", ref.text or "", re.I))
        association_ambiguous = ref.type == "TEXT_ONLY" and (numeric is not None or bool(re.search(r"(?:\d|[<>])\s+[A-Za-z]{2,}.*(?:\d|[<>])", after_unit)))
        conf = {"name":name.confidence,"value":.95 if numeric is not None or textual else .55,"unit":.95 if original_unit or textual else .55,"ref":.55 if ref.type == "UNKNOWN" else .95}
        if demographic_ambiguous or association_ambiguous: conf["ref"] = .55
        confidence = min(conf.values())
        item = LabResultItem(originalName=raw_name, normalizedName=name.normalized, value=numeric, valueText=value_text, valueOperator=operator.group(1) if operator else None, originalUnit=original_unit, normalizedUnit=self.units.normalize(original_unit), unit=self.units.normalize(original_unit), referenceMin=ref.minimum, referenceMax=ref.maximum, referenceType=ref.type, referenceOperator=ref.operator, referenceText=ref.text, reportedFlag=flag, section=section, methodText=method, page=page, confidence=round(confidence,2), fieldConfidences=conf, lowConfidence=confidence < .8, reviewRequired=name.ambiguous or demographic_ambiguous or association_ambiguous or confidence < .8, ambiguityReason=name.reason or ("demographic reference range requires patient metadata" if demographic_ambiguous else ("source row association is ambiguous" if association_ambiguous else None)))
        if flag and ((flag in {"L","LOW"} and ref.minimum is not None and numeric is not None and numeric >= ref.minimum) or (flag in {"H","HIGH"} and ref.maximum is not None and numeric is not None and numeric <= ref.maximum)):
            item.discrepancy = "reported flag disagrees with extracted reference range"; item.reviewRequired = True
        if tokens:
            x,y=min(t.x for t in tokens),min(t.y for t in tokens)
            item.boundingBoxJson=json.dumps({"x":x,"y":y,"width":max(t.x+t.width for t in tokens)-x,"height":max(t.y+t.height for t in tokens)-y,"page":page,"coordinateSpace":"rendered_pixels","dpi":300})
            item.fieldConfidences={k:min(v,min((t.confidence for t in tokens),default=0.)) for k,v in item.fieldConfidences.items()}
            item.confidence=round(min(item.fieldConfidences.values()),2); item.lowConfidence=item.confidence<.8; item.reviewRequired |= item.lowConfidence
        return item

    @staticmethod
    def _metadata(text: str) -> tuple[float | None, str | None]:
        ages = {float(m.group(1)) / (12 if (m.group(2) or "").lower().startswith("month") else 1) for m in re.finditer(r"\bage\s*[:\-]?\s*(\d+(?:\.\d+)?)\s*(years?|yrs?|months?)?\b", text, re.I)}
        sexes = {m.group(1).lower()[0] for m in re.finditer(r"\b(?:sex|gender)\s*[:\-]?\s*(male|female|m|f)\b", text, re.I)}
        return next(iter(ages)) if len(ages)==1 else None, next(iter(sexes)) if len(sexes)==1 else None

    @staticmethod
    def _number(value):
        try: return float(re.sub(r"[<>=≤≥]","",value).strip())
        except ValueError: return None
