from typing import Literal, Optional
from pydantic import BaseModel, Field

ExplanationMode = Literal["PATIENT_SIMPLE","CLINICIAN_SUMMARY","RESULT_EXPLANATION","REPORT_OVERVIEW"]

class StructuredResult(BaseModel):
    test: str
    value: Optional[float] = None
    valueText: str = ""
    unit: Optional[str] = None
    reference: Optional[str] = None
    status: str = "UNKNOWN"
    reportedFlag: Optional[str] = None
    reviewState: str = "AUTO_ACCEPTED"
    reviewRequired: bool = False
    validationIssues: list[dict] = Field(default_factory=list)
    section: Optional[str] = None

class ExplanationRequest(BaseModel):
    mode: ExplanationMode
    report: dict = Field(default_factory=dict)
    results: list[StructuredResult] = Field(default_factory=list)

class ExplanationResponse(BaseModel):
    summary: str
    validatedFindings: list[dict[str, str]] = Field(default_factory=list)
    requiresVerification: list[dict[str, str]] = Field(default_factory=list)
    disclaimer: str
    provider: str
    model: str
    promptVersion: str
    usedFallback: bool = False
