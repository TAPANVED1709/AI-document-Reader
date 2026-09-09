"""Exercise provider failures through real loopback HTTP and the FastAPI contract."""
import json
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import pytest
from fastapi.testclient import TestClient
from main import app


@pytest.mark.parametrize("failure", ["unreachable", "timeout", "invalid-json", "invalid-output", "invalid-field-types", "model-unavailable"])
def test_ollama_failure_returns_deterministic_response(monkeypatch, failure):
    calls = []

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *args):
            pass

        def do_POST(self):
            calls.append(self.path)
            if failure == "timeout":
                time.sleep(.15)
            self.send_response(404 if failure == "model-unavailable" else 200)
            self.end_headers()
            payload = b"invalid" if failure == "invalid-json" else json.dumps({"response": "{}"}).encode()
            if failure == "invalid-field-types":
                payload = json.dumps({"response": json.dumps({"summary": "Structured result", "validatedFindings": [{"test": "Hemoglobin", "value": 10.8}]})}).encode()
            try:
                self.wfile.write(payload)
            except BrokenPipeError:
                pass

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    port = server.server_port
    if failure == "unreachable":
        server.server_close()
    else:
        threading.Thread(target=server.serve_forever, daemon=True).start()
    monkeypatch.setenv("LOCAL_LLM_PROVIDER", "ollama")
    monkeypatch.setenv("LOCAL_LLM_MODEL", "synthetic-local-model")
    monkeypatch.setenv("LOCAL_LLM_BASE_URL", f"http://127.0.0.1:{port}")
    monkeypatch.setenv("LOCAL_LLM_TIMEOUT_SECONDS", ".03")
    try:
        response = TestClient(app).post("/explain", json={"mode": "PATIENT_SIMPLE", "results": [
            {"test": "Hemoglobin", "value": 10.8, "valueText": "10.8", "unit": "g/dL", "reference": "13 - 17", "status": "LOW"},
            {"test": "Uncertain", "reviewRequired": True, "reviewState": "REVIEW_REQUIRED"}]})
        assert response.status_code == 200
        data = response.json()
        assert data["provider"] == "deterministic" and data["usedFallback"] is True
        assert len(data["validatedFindings"]) == 1
        assert data["requiresVerification"][0]["test"] == "Uncertain"
        if failure != "unreachable":
            assert calls == ["/api/generate"]
    finally:
        if failure != "unreachable":
            server.shutdown()
            server.server_close()


def test_invalid_explanation_input_remains_4xx():
    response = TestClient(app).post("/explain", json={"mode": "INVALID"})
    assert response.status_code == 422


def test_compose_ollama_address_is_local_and_allowed():
    from explanations.provider import OllamaProvider
    assert OllamaProvider(base_url="http://ollama:11434", model="synthetic").base_url == "http://ollama:11434"


@pytest.mark.parametrize("url", ["http://localhost:11434@evil.example", "http://ollama.evil.example:11434", "https://example.com"])
def test_provider_rejects_nonlocal_destinations(url):
    from explanations.provider import OllamaProvider
    with pytest.raises(ValueError):
        OllamaProvider(base_url=url, model="synthetic")
