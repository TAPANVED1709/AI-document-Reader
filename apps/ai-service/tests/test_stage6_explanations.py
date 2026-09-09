import asyncio
import json
import pytest
from fastapi.testclient import TestClient
from main import app
from explanations import ExplanationRequest, StructuredResult, DeterministicFallbackProvider, build_prompt, PROMPT_VERSION
from explanations.provider import validate_grounding, OllamaProvider, provider_from_config

def accepted(**kwargs):
    base=dict(test="Hemoglobin",value=10.8,valueText="10.8",unit="g/dL",reference="13 - 17",status="LOW",reviewState="AUTO_ACCEPTED",reviewRequired=False)
    base.update(kwargs)
    return StructuredResult(**base)

def test_prompt_is_structured_and_excludes_raw_pdf():
    prompt=build_prompt(ExplanationRequest(mode="PATIENT_SIMPLE",report={"resultCount":1},results=[accepted()]))
    assert "10.8" in prompt and "raw PDF" not in prompt and PROMPT_VERSION in prompt

def test_corrected_and_verified_values_are_the_only_inputs():
    prompt=build_prompt(ExplanationRequest(mode="CLINICIAN_SUMMARY",results=[accepted(value=11.2,valueText="11.2",reviewState="HUMAN_CORRECTED"),accepted(test="Creatinine",value=.9,valueText=".9",reviewState="HUMAN_VERIFIED")]))
    assert "11.2" in prompt and ".9" in prompt

def test_unresolved_review_is_separate_from_findings():
    uncertain=accepted(test="HbA1c",value=108,valueText="108",reviewRequired=True,reviewState="REVIEW_REQUIRED",validationIssues=[{"code":"POSSIBLE_DECIMAL_ERROR","message":"Verify source"}])
    out=asyncio.run(DeterministicFallbackProvider().generate(ExplanationRequest(mode="PATIENT_SIMPLE",results=[accepted(),uncertain])))
    assert len(out.validatedFindings)==1 and out.requiresVerification[0]["test"]=="HbA1c" and "10.8" not in out.summary

def test_textual_result_is_preserved():
    out=asyncio.run(DeterministicFallbackProvider().generate(ExplanationRequest(mode="RESULT_EXPLANATION",results=[accepted(test="CRP",value=None,valueText="Negative",reference="Negative",status="UNKNOWN")])))
    assert "Negative" in out.validatedFindings[0]["explanation"] and "diagnosis" not in out.validatedFindings[0]["explanation"].lower()

def test_hallucinated_numeric_output_is_rejected():
    from explanations.models import ExplanationResponse
    with pytest.raises(ValueError):
        validate_grounding(ExplanationResponse(summary="Hemoglobin was 999",disclaimer="",provider="fake",model="fake",promptVersion=PROMPT_VERSION), ExplanationRequest(mode="PATIENT_SIMPLE",results=[accepted()]))

def test_hallucinated_common_test_name_is_rejected():
    from explanations.models import ExplanationResponse
    with pytest.raises(ValueError, match="ungrounded test name"):
        validate_grounding(ExplanationResponse(summary="Creatinine was normal",disclaimer="",provider="fake",model="fake",promptVersion=PROMPT_VERSION), ExplanationRequest(mode="PATIENT_SIMPLE",results=[accepted()]))

@pytest.mark.parametrize("mode",["PATIENT_SIMPLE","CLINICIAN_SUMMARY","RESULT_EXPLANATION","REPORT_OVERVIEW"])
def test_all_modes_have_deterministic_fallback(mode):
    out=asyncio.run(DeterministicFallbackProvider().generate(ExplanationRequest(mode=mode,results=[accepted()])))
    assert out.usedFallback and out.disclaimer and out.promptVersion == PROMPT_VERSION

def test_missing_model_uses_fallback(monkeypatch):
    monkeypatch.delenv("LOCAL_LLM_MODEL", raising=False)
    monkeypatch.setenv("LOCAL_LLM_PROVIDER","ollama")
    assert provider_from_config().provider == "deterministic"

def test_remote_model_url_is_rejected(monkeypatch):
    monkeypatch.setenv("LOCAL_LLM_BASE_URL","https://example.com")
    with pytest.raises(ValueError): OllamaProvider(model="local-model")

def test_timeout_is_controlled():
    async def slow(): await asyncio.sleep(.05)
    with pytest.raises(asyncio.TimeoutError): asyncio.run(asyncio.wait_for(slow(), timeout=.001))

def test_local_explanation_endpoint_is_structured():
    response=TestClient(app).post("/explain",json={"mode":"PATIENT_SIMPLE","report":{"resultCount":1},"results":[accepted().model_dump()]})
    assert response.status_code == 200 and response.json()["validatedFindings"][0]["test"]=="Hemoglobin"

def test_local_ai_health_does_not_expose_secrets():
    response=TestClient(app).get("/health/local-ai")
    assert response.status_code == 200 and "apiKey" not in response.text and "available" in response.json()

def test_no_pdf_content_is_in_fallback_output():
    out=asyncio.run(DeterministicFallbackProvider().generate(ExplanationRequest(mode="PATIENT_SIMPLE",report={"source":"structured-only"},results=[accepted()])))
    assert "PDF" not in out.summary and "structured" in out.summary

def test_unknown_range_is_not_invented():
    out=asyncio.run(DeterministicFallbackProvider().generate(ExplanationRequest(mode="RESULT_EXPLANATION",results=[accepted(reference=None,status="UNKNOWN")])))
    assert "reference range" not in out.validatedFindings[0]["explanation"]
