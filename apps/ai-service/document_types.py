from dataclasses import dataclass
import re

LAB = "LAB_REPORT"
DISCHARGE = "DISCHARGE_SUMMARY"
PRESCRIPTION = "PRESCRIPTION"
RADIOLOGY = "RADIOLOGY_REPORT"
UNKNOWN = "UNKNOWN"

@dataclass(frozen=True)
class DocumentClassification:
    document_type: str
    confidence: float
    signals: list[str]

def classify_document(text: str) -> DocumentClassification:
    value = text.lower()
    lab_rows = re.findall(r"\b(?:[<>]=?)?\s*(?:\d+(?:\.\d+)?|\.\d+)\s*[a-z%/µ³^]+\s+(?:\d+(?:\.\d+)?|\.\d+)\s*[-–]\s*(?:\d+(?:\.\d+)?|\.\d+)", value)
    if len(lab_rows) >= 2:
        return DocumentClassification(LAB, 0.9, ["numeric result with unit and reference interval"])
    pathology_signals = re.findall(r"\b(?:cbc|hemogram|lft|kft|rft|lipid profile|thyroid profile|diabetes profile|iron studies|urine routine|urine examination|coagulation|electrolytes|serology|hematology|biochemistry)\b", value)
    if len(pathology_signals) >= 1 and (lab_rows or re.search(r"\b(?:result|reference range|unit|test|negative|positive|non[- ]?reactive|absent|present)\b", value)):
        return DocumentClassification(LAB, 0.86, ["pathology panel heading"])
    if re.search(r"\blaboratory report\b", value) and re.search(r"\b(?:age|sex|gender)\s*[:\-]", value):
        return DocumentClassification(LAB, 0.84, ["laboratory report demographics"])
    if re.search(r"^\s*(?:prescription|rx)\s*$", value, re.I | re.M):
        return DocumentClassification(PRESCRIPTION, 0.88, ["prescription heading"])
    patterns = {
        LAB: [r"laboratory report", r"reference range", r"biological reference", r"\btest\b.*\bresult\b"],
        DISCHARGE: [r"discharge summary", r"date of admission", r"date of discharge", r"hospital course", r"discharge medication", r"follow[ -]?up"],
        PRESCRIPTION: [r"\bprescription\b", r"\brx\b", r"\bmedicine\b", r"\bdose\b", r"\bfrequency\b", r"\bduration\b"],
        RADIOLOGY: [r"radiology", r"\bct\b", r"\bmri\b", r"x[ -]?ray", r"ultrasound", r"\bfindings\b", r"\bimpression\b", r"\btechnique\b"],
    }
    scores = {kind: [signal for signal, pattern in enumerate(items) if re.search(pattern, value, re.I)] for kind, items in patterns.items()}
    ranked = sorted(scores.items(), key=lambda item: len(item[1]), reverse=True)
    if not ranked or len(ranked[0][1]) < 2 or (len(ranked) > 1 and len(ranked[0][1]) == len(ranked[1][1])):
        return DocumentClassification(UNKNOWN, 0.0, [])
    kind, hits = ranked[0]
    signal_names = [patterns[kind][i] for i in hits]
    return DocumentClassification(kind, min(0.99, 0.65 + 0.08 * len(hits)), signal_names)
