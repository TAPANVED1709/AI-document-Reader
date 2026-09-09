from pydantic import BaseModel, Field
from typing import Optional

class ValidationIssue(BaseModel):
    code: str
    severity: str
    field: Optional[str] = None
    message: str
    requiresReview: bool = True
    source: str = "VALIDATION"
    isResolved: bool = False
    resolutionType: Optional[str] = None

class ValidationConfig(BaseModel):
    valueConfidence: float = Field(0.80, ge=0, le=1)
    referenceConfidence: float = Field(0.80, ge=0, le=1)
    nameConfidence: float = Field(0.80, ge=0, le=1)
    associationConfidence: float = Field(0.80, ge=0, le=1)
