import asyncio
import json
import os
import re
from abc import ABC, abstractmethod
from urllib.request import Request, urlopen
from urllib.error import URLError
from urllib.parse import urlsplit
from .models import ExplanationRequest, ExplanationResponse, StructuredResult
from .prompts import build_prompt, PROMPT_VERSION

DISCLAIMER = "AI-generated explanation from validated extracted report data. Informational only; not a diagnosis or treatment recommendation."
class LocalExplanationProvider(ABC):
    provider = "local"
    model = "deterministic-fallback"
    @abstractmethod
    async def generate(self, request: ExplanationRequest) -> ExplanationResponse: ...

class DeterministicFallbackProvider(LocalExplanationProvider):
    provider = "deterministic"
    model = "template"
    async def generate(self, request):
        findings, verify = [], []
        for result in request.results:
            if result.reviewRequired or result.reviewState == "REVIEW_REQUIRED":
                verify.append({"test": result.test, "reason": "; ".join(i.get("message", "Source verification required.") for i in result.validationIssues) or "This extraction requires verification against the source report."})
                continue
            value = result.valueText or (str(result.value) if result.value is not None else "not available")
            ref = f" The report reference range is {result.reference}." if result.reference else ""
            if request.mode == "CLINICIAN_SUMMARY":
                text = f"{result.test}: {value}{(' ' + result.unit) if result.unit else ''}; classifier status {result.status}.{ref}"
            elif request.mode == "REPORT_OVERVIEW":
                text = f"{result.test} is classified as {result.status}."
            else:
                text = f"The report records {result.test} as {value}{(' ' + result.unit) if result.unit else ''}.{ref} The validated classifier marks it {result.status}."
            findings.append({"test": result.test, "explanation": text})
        if request.mode == "REPORT_OVERVIEW":
            counts = {s: sum(1 for r in request.results if not r.reviewRequired and r.status == s) for s in ("NORMAL","HIGH","LOW","UNKNOWN")}
            summary = f"The report contains {len(request.results)} extracted results: {counts['NORMAL']} within range, {counts['HIGH']} high, {counts['LOW']} low, and {counts['UNKNOWN']} unknown."
        else:
            summary = "The explanation uses only validated structured report data."
        return ExplanationResponse(summary=summary, validatedFindings=findings, requiresVerification=verify, disclaimer=DISCLAIMER, provider=self.provider, model=self.model, promptVersion=PROMPT_VERSION, usedFallback=True)

class OllamaProvider(LocalExplanationProvider):
    provider = "ollama"
    def __init__(self, base_url=None, model=None, timeout=None):
        self.base_url = (base_url or os.getenv("LOCAL_LLM_BASE_URL","http://127.0.0.1:11434")).rstrip("/")
        self.model = model or os.getenv("LOCAL_LLM_MODEL","")
        self.timeout = float(timeout or os.getenv("LOCAL_LLM_TIMEOUT_SECONDS","30"))
        endpoint = urlsplit(self.base_url)
        if (endpoint.scheme != "http" or endpoint.hostname not in {"127.0.0.1", "localhost", "ollama"}
                or endpoint.username or endpoint.password or endpoint.path not in {"", "/"}
                or endpoint.query or endpoint.fragment):
            raise ValueError("Local explanation provider must use localhost.")
    async def generate(self, request):
        if not self.model: raise RuntimeError("LOCAL_LLM_MODEL is not configured.")
        body=json.dumps({"model":self.model,"prompt":build_prompt(request),"stream":False,"format":"json","options":{"temperature":0.2}}).encode()
        def call():
            with urlopen(Request(self.base_url+"/api/generate", body, {"Content-Type":"application/json"}), timeout=self.timeout) as response:
                return json.loads(response.read().decode())["response"]
        raw=await asyncio.to_thread(call)
        data=json.loads(raw)
        output=ExplanationResponse(provider=self.provider, model=self.model, promptVersion=PROMPT_VERSION, disclaimer=data.get("disclaimer",DISCLAIMER), summary=data["summary"], validatedFindings=data.get("validatedFindings",[]), requiresVerification=data.get("requiresVerification",[]))
        validate_grounding(output, request)
        return output

def validate_grounding(output, request):
    allowed_numbers={str(x) for r in request.results for x in [r.value] if x is not None}
    allowed_numbers |= {n for r in request.results for n in re.findall(r"[-+]?\d+(?:\.\d+)?", r.reference or "")}
    text=json.dumps({"summary": output.summary, "validatedFindings": output.validatedFindings, "requiresVerification": output.requiresVerification})
    for n in re.findall(r"[-+]?\d+(?:\.\d+)?", text):
        if n not in allowed_numbers: raise ValueError("Explanation contains an ungrounded numeric value.")
    known={r.test.lower() for r in request.results}
    common_tests={"hemoglobin","hba1c","creatinine","tsh","glucose","crp","cholesterol","bilirubin","platelets","wbc"}
    for test_name in common_tests:
        if test_name in text.lower() and not any(test_name in name for name in known):
            raise ValueError("Explanation contains an ungrounded test name.")
    return output

def provider_from_config():
    if os.getenv("LOCAL_LLM_PROVIDER","ollama").lower() == "ollama" and os.getenv("LOCAL_LLM_MODEL","").strip():
        try: return OllamaProvider()
        except ValueError: pass
    return DeterministicFallbackProvider()
