from __future__ import annotations
from dataclasses import asdict, dataclass, field
import re

@dataclass
class SourceInfo:
    pageNumber: int = 1
    sourceType: str = "NATIVE_TEXT"
    originalText: str = ""
    extractionConfidence: float = 0.95
    reviewRequired: bool = False

@dataclass
class MedicationEntry:
    medicineName: str
    originalMedicineName: str
    normalizedMedicineName: str
    strength: str | None = None
    dosePattern: str | None = None
    route: str | None = None
    originalFrequency: str | None = None
    normalizedFrequency: str | None = None
    duration: str | None = None
    timing: str | None = None
    instructionText: str = ""
    source: SourceInfo = field(default_factory=SourceInfo)

def _source(text: str, review=False):
    return SourceInfo(originalText=text, extractionConfidence=0.55 if review else 0.95, reviewRequired=review)

def _date(text, labels):
    pattern = r"(?:" + "|".join(labels) + r")\s*[:\-]?\s*(\d{1,2}[/-]\d{1,2}[/-]\d{2,4}|\d{4}[/-]\d{1,2}[/-]\d{1,2})"
    match = re.search(pattern, text, re.I)
    return match.group(1) if match else None

def _section(text, names):
    match = re.search(r"(?:^|\n)\s*(?:" + "|".join(names) + r")\s*:\s*(.*?)(?=\n\s*[A-Z][A-Za-z /-]{2,}:|\Z)", text, re.I | re.S)
    return match.group(1).strip() if match else None

def parse_discharge(text: str):
    medications = parse_medications(_section(text, ["Discharge Medication[s]?", "Medications"]) or "")
    return {"hospitalName": text.splitlines()[0].strip() if text.splitlines() else None, "admissionDate": _date(text, ["Date of Admission", "Admission Date"]), "dischargeDate": _date(text, ["Date of Discharge", "Discharge Date"]), "documentStatedDiagnosis": _section(text, ["Diagnosis", "Diagnoses"]), "reasonForAdmission": _section(text, ["Reason for Admission"]), "procedures": _section(text, ["Procedures"]), "investigations": _section(text, ["Investigations"]), "hospitalCourse": _section(text, ["Hospital Course"]), "conditionAtDischarge": _section(text, ["Condition at Discharge"]), "dischargeMedications": [asdict(item) for item in medications], "followUpInstructions": _section(text, ["Follow Up", "Follow-up"]), "dietActivityInstructions": _section(text, ["Diet", "Activity"]), "warnings": _section(text, ["Warnings", "Warning"]), "originalText": text, "reviewRequired": not bool(_section(text, ["Diagnosis", "Diagnoses"]))}

def _frequency(value):
    normalized = {"od":"once daily", "bd":"twice daily", "bid":"twice daily", "tds":"three times daily", "tid":"three times daily", "qid":"four times daily", "sos":"as needed", "prn":"as needed", "hs":"at bedtime"}
    return normalized.get(value.lower(), value)

def parse_medications(text: str):
    entries = []
    lines = [line.strip(" -•\t") for line in text.splitlines() if line.strip()]
    index = 0
    while index < len(lines):
        raw = lines[index]
        if raw.lower().rstrip(":") in {"prescription", "medications", "discharge medications", "rx"}:
            index += 1
            continue
        for continuation in lines[index + 1:index + 4]:
            if re.search(r"(?:tab(?:let)?\.?|cap(?:sule)?\.?|syrup)?\s*[A-Za-z][A-Za-z0-9-]*\s+\d+(?:\.\d+)?\s*(?:mg|mcg|g|ml|units?)\b", continuation, re.I):
                break
            if continuation.lower().rstrip(":") in {"prescription", "medications", "discharge medications", "rx"}:
                break
            raw += " " + continuation
        match = re.search(r"(?:tab(?:let)?\.?|cap(?:sule)?\.?|syrup)?\s*([A-Za-z][A-Za-z0-9-]*)\s*(\d+(?:\.\d+)?\s*(?:mg|mcg|g|ml|units?)\b)?", raw, re.I)
        if not match:
            index += 1
            continue
        name, strength = match.group(1), match.group(2)
        dose = re.search(r"\b(\d+-\d+-\d+|OD|BD|BID|TDS|TID|QID|SOS|PRN|HS)\b", raw, re.I)
        duration = re.search(r"\b(\d+\s*(?:days?|weeks?|months?))\b", raw, re.I)
        timing = re.search(r"\b(after food|before food|with food|at night)\b", raw, re.I)
        ambiguous = name.lower() in {"tab", "tablet", "medicine", "drug"} or not strength
        frequency = dose.group(1) if dose else None
        entries.append(MedicationEntry(name, name, name, strength, frequency if frequency and "-" in frequency else None, None, frequency, _frequency(frequency) if frequency else None, duration.group(1) if duration else None, timing.group(1) if timing else None, raw, _source(raw, ambiguous)))
        index += 1
    return entries

def parse_prescription(text: str):
    medications = parse_medications(text)
    return {"prescriber": _section(text, ["Prescriber", "Doctor"]), "prescriptionDate": _date(text, ["Prescription Date", "Date"]), "medications": [asdict(item) for item in medications], "originalText": text, "reviewRequired": any(item.source.reviewRequired for item in medications)}

def parse_radiology(text: str):
    first = next((line.strip() for line in text.splitlines() if line.strip()), "")
    modality_match = re.search(r"\b(CT|MRI|X[- ]?Ray|Ultrasound)\b", text, re.I)
    modality = modality_match.group(1).upper().replace("X-RAY", "X-RAY") if modality_match else None
    study = first or None
    body = re.sub(r"\b(CT|MRI|X[- ]?Ray|Ultrasound)\b", "", first, flags=re.I).strip(" :-") or None
    return {"modality": modality, "bodyRegion": body, "studyName": study, "studyDate": _date(text, ["Study Date", "Date"]), "technique": _section(text, ["Technique"]), "comparison": _section(text, ["Comparison"]), "findings": _section(text, ["Findings"]), "impression": _section(text, ["Impression"]), "recommendation": _section(text, ["Recommendation", "Recommendations"]), "originalText": text, "reviewRequired": not bool(_section(text, ["Findings"]) and _section(text, ["Impression"]))}
