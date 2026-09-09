import re
import unicodedata
from dataclasses import dataclass
from difflib import SequenceMatcher


@dataclass(frozen=True)
class NameMatch:
    original: str
    normalized: str
    confidence: float
    ambiguous: bool = False
    reason: str | None = None


class TestNameNormalizer:
    __test__ = False
    GROUPS = {
        "Hemoglobin": ["hemoglobin", "haemoglobin", "hb", "hgb"],
        "RBC": ["rbc", "red blood cell count", "erythrocyte count"],
        "WBC": ["wbc", "tlc", "total leukocyte count", "white blood cell count"],
        "Platelet Count": ["platelet count", "platelets", "plt"],
        "Hematocrit": ["hematocrit", "haematocrit", "hct", "pcv"],
        "MCV": ["mcv"], "MCH": ["mch"], "MCHC": ["mchc"], "RDW": ["rdw"],
        "Neutrophils": ["neutrophils", "neutrophil %", "neut %"], "Lymphocytes": ["lymphocytes", "lymphocyte %", "lymph %"],
        "Monocytes": ["monocytes"], "Eosinophils": ["eosinophils"], "Basophils": ["basophils"],
        "Glucose": ["glucose", "blood glucose", "fasting glucose", "fasting blood sugar", "fbs", "fasting plasma glucose", "ppbs", "post prandial blood sugar", "random blood sugar", "rbs"],
        "HbA1c": ["hba1c", "glycated hemoglobin", "glycosylated hemoglobin"],
        "Monocytes": ["monocytes", "monocyte %", "mono %"], "Eosinophils": ["eosinophils", "eosinophil %", "eos %"], "Basophils": ["basophils", "basophil %", "baso %"],
        "Absolute Neutrophils": ["absolute neutrophil count", "anc"], "Absolute Lymphocytes": ["absolute lymphocyte count", "alc"], "Absolute Eosinophils": ["absolute eosinophil count", "aec"],
        "Creatinine": ["creatinine", "serum creatinine"], "Urea": ["urea", "blood urea"],
        "BUN": ["bun", "blood urea nitrogen"], "Uric Acid": ["uric acid"], "eGFR": ["egfr"],
        "AST": ["ast", "sgot", "aspartate aminotransferase"], "ALT": ["alt", "sgpt", "alanine aminotransferase"], "GGT": ["ggt", "gamma gt", "gamma glutamyl transferase"],
        "ALP": ["alp", "alkaline phosphatase"], "Total Bilirubin": ["total bilirubin", "bilirubin total"],
        "Direct Bilirubin": ["direct bilirubin"], "Indirect Bilirubin": ["indirect bilirubin"],
        "Albumin": ["albumin"], "Globulin": ["globulin"], "Total Protein": ["total protein"], "A/G Ratio": ["a/g ratio", "albumin globulin ratio"],
        "Total Cholesterol": ["total cholesterol", "cholesterol"], "HDL": ["hdl", "hdl cholesterol"],
        "LDL": ["ldl", "ldl cholesterol"], "VLDL": ["vldl"], "Triglycerides": ["triglycerides", "tg"],
        "TC/HDL Ratio": ["tc/hdl ratio"], "LDL/HDL Ratio": ["ldl/hdl ratio"],
        "TSH": ["tsh", "thyroid stimulating hormone"], "T3": ["t3", "total t3"], "Free T3": ["free t3", "ft3"],
        "T4": ["t4", "total t4"], "Free T4": ["free t4", "ft4"],
        "Vitamin B12": ["vitamin b12", "vit b12", "b12"], "Vitamin D": ["vitamin d", "25-oh vitamin d", "25 hydroxy vitamin d", "25(oh)d"], "Folate": ["folate", "folic acid"],
        "Iron": ["iron", "serum iron"], "Ferritin": ["ferritin"], "TIBC": ["tibc", "total iron binding capacity"], "UIBC": ["uibc", "unsaturated iron binding capacity"], "Transferrin Saturation": ["transferrin saturation", "tsat"],
        "Sodium": ["sodium", "na"], "Potassium": ["potassium", "k"], "Chloride": ["chloride", "cl"], "Calcium": ["calcium", "ca"], "Magnesium": ["magnesium", "mg"], "Phosphorus": ["phosphorus", "phosphate"],
        "CRP": ["crp", "c-reactive protein"], "hs-CRP": ["hs-crp", "high sensitivity crp"], "ESR": ["esr", "erythrocyte sedimentation rate"], "PT": ["pt", "prothrombin time"], "INR": ["inr"], "aPTT": ["aptt", "a PTT", "activated partial thromboplastin time"],
        "pH": ["ph"], "Specific Gravity": ["specific gravity", "sp gr"], "Protein": ["protein"], "Ketones": ["ketones"], "Nitrite": ["nitrite"], "Leukocyte Esterase": ["leukocyte esterase"], "RBC/hpf": ["rbc/hpf", "rbc per hpf"], "WBC/hpf": ["wbc/hpf", "wbc per hpf"],
        "HBsAg": ["hbsag", "hepatitis b surface antigen"], "HIV": ["hiv", "hiv screening"], "HCV": ["hcv", "hepatitis c"], "VDRL": ["vdrl"], "Rheumatoid Factor": ["rheumatoid factor", "rf"],
        "Procalcitonin": ["procalcitonin"],
    }

    def __init__(self):
        self.aliases = {self._key(alias): canonical for canonical, aliases in self.GROUPS.items() for alias in aliases}
        self._ordered = sorted(self.aliases, key=len, reverse=True)

    @staticmethod
    def _key(value: str) -> str:
        value = unicodedata.normalize("NFKC", value).replace("İ", "I")
        value = value.translate(str.maketrans({"–": "-", "—": "-", "−": "-"}))
        return re.sub(r"[^a-z0-9%/]+", " ", value.lower()).strip()

    def normalize(self, original: str) -> NameMatch:
        clean = " ".join(unicodedata.normalize("NFKC", original).split()).strip()
        key = self._key(clean)
        if key in self.aliases:
            return NameMatch(clean, self.aliases[key], 1.0)
        # Only repair a small, explicit OCR alphabet confusion set.
        repaired = key.translate(str.maketrans({"1": "i", "0": "o", "l": "i"}))
        corrupt = {"hemogiobin": "hemoglobin", "haemogiobin": "haemoglobin", "creatlnine": "creatinine", "creatinlne": "creatinine", "cholesteroi": "cholesterol", "bi i irubin": "bilirubin", "bilirubln": "bilirubin", "aik": "alk", "aikallne phosphatase": "alkaline phosphatase"}
        if key in corrupt and corrupt[key] in self.aliases:
            return NameMatch(clean, self.aliases[corrupt[key]], 0.88)
        exact = [a for a in self._ordered if repaired == a or repaired.startswith(a + " ")]
        if exact:
            return NameMatch(clean, self.aliases[exact[0]], 0.91)
        close = [(SequenceMatcher(None, key, alias).ratio(), alias) for alias in self._ordered if len(alias) >= 4]
        close = sorted(close, reverse=True)
        if close and close[0][0] >= 0.88 and (len(close) == 1 or close[0][0] - close[1][0] >= 0.04):
            return NameMatch(clean, self.aliases[close[0][1]], close[0][0])
        if close and close[0][0] >= 0.78 and len(close) > 1 and close[0][0] - close[1][0] < 0.04:
            return NameMatch(clean, clean, close[0][0], True, "test name matched multiple aliases")
        return NameMatch(clean, clean, 0.7 if clean else 0.0)



