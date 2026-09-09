import json
from .models import ExplanationRequest
PROMPT_VERSION = "stage6-safety-v1"
SYSTEM_PROMPT = """You explain structured medical-report information. You do not diagnose diseases or recommend medication, dosage, or treatment. Do not alter extracted values or invent reference ranges. Only describe HIGH, LOW, NORMAL, or UNKNOWN when supplied in validated input. If ReviewRequired is true and unresolved, say it must be verified against the source report before interpretation. Distinguish report facts from general education and do not claim medical certainty. Return only JSON with summary, validatedFindings, requiresVerification, and disclaimer."""
def build_prompt(request: ExplanationRequest) -> str:
    safe = request.model_copy(deep=True)
    safe.results = [r for r in safe.results if not r.reviewRequired and r.reviewState not in {"REVIEW_REQUIRED"}]
    payload = {"mode": request.mode, "report": request.report, "results": [r.model_dump() for r in safe.results], "requiresVerification": [r.model_dump() for r in request.results if r.reviewRequired or r.reviewState == "REVIEW_REQUIRED"]}
    return SYSTEM_PROMPT + "\nPROMPT_VERSION=" + PROMPT_VERSION + "\nSTRUCTURED_INPUT=" + json.dumps(payload, sort_keys=True, separators=(",", ":"))
