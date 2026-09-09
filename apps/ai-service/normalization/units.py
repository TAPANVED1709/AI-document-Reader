import re
import unicodedata


class UnitNormalizer:
    MAP = {
        "gm/dl": "g/dL", "g/dl": "g/dL", "mg/dl": "mg/dL",
        "mmol/l": "mmol/L", "meq/l": "mEq/L", "iu/l": "IU/L", "u/l": "U/L", "µiu/ml": "µIU/mL", "uiu/ml": "µIU/mL", "miu/l": "mIU/L",
        "10^3/ul": "10³/µL", "x10^3/ul": "10³/µL", "10³/µl": "10³/µL", "x10³/ul": "10³/µL", "k/ul": "10³/µL", "x10^3/µl": "10³/µL",
        "pg": "pg", "pg/ml": "pg/mL", "ng/ml": "ng/mL", "ng/dl": "ng/dL", "µg/dl": "µg/dL", "ug/dl": "µg/dL", "mcg/dl": "µg/dL",
        "mm/hr": "mm/hr", "sec": "sec", "secs": "sec", "second": "sec", "seconds": "sec",
        "%": "%", "mg/l": "mg/L", "ug/l": "µg/L", "fl": "fL", "ml/min": "mL/min", "ratio": "ratio",
    }

    def normalize(self, original: str | None) -> str | None:
        if not original:
            return None
        value = unicodedata.normalize("NFKC", original).strip().replace("μ", "µ")
        key = re.sub(r"\s+", "", value).lower()
        return self.MAP.get(key, value)
