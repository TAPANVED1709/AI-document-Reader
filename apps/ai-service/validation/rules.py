import os
from .models import ValidationConfig

def load_config() -> ValidationConfig:
    def number(name, default):
        try: return float(os.getenv(name, default))
        except (TypeError, ValueError): return default
    return ValidationConfig(
        valueConfidence=number("VALUE_CONFIDENCE_REVIEW_THRESHOLD", .80),
        referenceConfidence=number("REFERENCE_CONFIDENCE_REVIEW_THRESHOLD", .80),
        nameConfidence=number("NAME_CONFIDENCE_REVIEW_THRESHOLD", .80),
        associationConfidence=number("ASSOCIATION_CONFIDENCE_REVIEW_THRESHOLD", .80),
    )
