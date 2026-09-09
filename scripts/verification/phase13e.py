"""Reproducible, isolated Phase 13E Docker verification with synthetic data only.

Runs with python scripts/verification/phase13e.py. Secrets and fixtures live in a
temporary directory, Compose uses a unique phase13e project, and volumes are retained.
Only loopback API ports are published. No existing project containers are stopped.
"""
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import platform
import secrets
import subprocess
import tempfile
import time
import uuid

import fitz
import httpx

ROOT = Path(__file__).resolve().parents[2]
PROJECT = "phase13e-" + secrets.token_hex(4)
BASE = "http://127.0.0.1:18080"
RESULTS = {"phase": "13E", "checks": {}}


def log(message):
    print(message, flush=True)


def run(args, **kwargs):
    result = subprocess.run(args, cwd=ROOT, text=True, capture_output=True, timeout=600, **kwargs)
    if result.returncode:
        raise RuntimeError(f"Command failed ({args[0]}): {result.stderr[-2000:]}")
    return result.stdout


def wait_for(check, timeout=180):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        value = check()
        if value:
            return value
        time.sleep(.2)
    raise TimeoutError("Verification condition was not met")


def main():
    with tempfile.TemporaryDirectory(prefix="phase13e-") as temporary:
        temp = Path(temporary)
        password = "Phase13e!" + secrets.token_hex(16)
        envfile = temp / "compose.env"
        envfile.write_text(f"MSSQL_SA_PASSWORD={password}\nConnectionStrings__DefaultConnection=Server=sqlserver,1433;Database=AiDocumentReaderDb;User Id=sa;Password={password};TrustServerCertificate=True;\n")
        override = temp / "override.yml"
        override.write_text('''services:
  api:
    ports: ["127.0.0.1:18080:8080"]
    environment:
      Processing__MaxConcurrentJobs: "2"
      Processing__RetryBaseSeconds: "20"
      Security__AuthRequestsPerMinute: "100"
      Logging__LogLevel__Microsoft.EntityFrameworkCore: Warning
  ai-service:
    ports: ["127.0.0.1:18000:8000"]
    environment:
      LOCAL_LLM_PROVIDER: ollama
      LOCAL_LLM_BASE_URL: http://127.0.0.1:11434
      LOCAL_LLM_MODEL: phase13e-unavailable-model
      LOCAL_LLM_TIMEOUT_SECONDS: "1"
''')
        compose = ["docker", "compose", "-p", PROJECT, "--env-file", str(envfile), "-f", str(ROOT / "docker-compose.yml"), "-f", str(override)]

        def dc(*args):
            return run(compose + list(args))

        def sql(query):
            script = temp / "query.sql"
            script.write_text("SET NOCOUNT ON;\n" + query, encoding="utf-8")
            run(["docker", "cp", str(script), f"{PROJECT}-sqlserver-1:/tmp/phase13e.sql"])
            raw = dc("exec", "-T", "sqlserver", "sh", "-c", 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d AiDocumentReaderDb -w 65535 -y 0 -i /tmp/phase13e.sql')
            return raw.strip()

        def query_json(query):
            raw = "".join(sql(query + " FOR JSON PATH").splitlines())
            return json.loads(raw[raw.index("["):raw.rindex("]") + 1])

        def make_fixture(kind):
            native = fitz.open()
            page = native.new_page()
            page.insert_text((60, 60), "Synthetic pathology laboratory report", fontsize=16)
            for index, row in enumerate(["Hemoglobin 10.8 g/dL 13.0 - 17.0", "Creatinine 0.9 mg/dL 0.7 - 1.3", "HbA1c 6.8 % 4.0 - 5.6"]):
                page.insert_text((60, 120 + index * 45), row, fontsize=16)
            if kind == "native":
                data = native.tobytes()
            else:
                doc = fitz.open()
                if kind == "hybrid":
                    doc.insert_pdf(native)
                image = page.get_pixmap(dpi=300).tobytes("png")
                doc.new_page(width=page.rect.width, height=page.rect.height).insert_image(page.rect, stream=image)
                if kind == "hybrid":
                    extra = fitz.open()
                    p = extra.new_page()
                    p.insert_text((60, 120), "Glucose 90 mg/dL 70 - 110\nCholesterol 180 mg/dL 100 - 200", fontsize=16)
                    doc.insert_pdf(extra)
                    extra.close()
                data = doc.tobytes(deflate=True)
                doc.close()
            native.close()
            return data

        session = httpx.Client(trust_env=False)

        def request(method, path, **kwargs):
            response = session.request(method, BASE + path, timeout=120, **kwargs)
            response.raise_for_status()
            return response

        def csrf():
            session.headers["X-XSRF-TOKEN"] = request("GET", "/api/auth/csrf").json()["token"]

        def ingest(kind, key):
            response = request("POST", "/api/lab-ingestion/reports", headers={"Idempotency-Key": key},
                               data={"patientId": patient_id}, files={"file": (f"synthetic-{kind}.pdf", fixtures[kind], "application/pdf")})
            assert response.status_code == 202
            return response.json()

        def status(job):
            return request("GET", f"/api/reports/{job['reportId']}/processing-status").json()

        def terminal(job):
            return wait_for(lambda: (s if (s := status(job))["status"] in {"COMPLETED", "REVIEW_REQUIRED", "FAILED"} else None))

        def report(job):
            return request("GET", f"/api/reports/{job['reportId']}").json()

        def assert_single_set(job):
            report_id = str(uuid.UUID(job["reportId"]))
            counts = query_json(f"SELECT (SELECT COUNT(*) FROM ProcessingJobs WHERE ReportId='{report_id}') jobs, (SELECT COUNT(*) FROM AnalysisRuns WHERE MedicalReportId='{report_id}') analysisRuns, (SELECT COUNT(*) FROM LabResults WHERE MedicalReportId='{report_id}') results, (SELECT COUNT(*) FROM ValidationIssues v JOIN LabResults l ON l.Id=v.LabResultId WHERE l.MedicalReportId='{report_id}') issues")[0]
            assert counts["jobs"] == counts["analysisRuns"] == 1
            duplicate = query_json(f"SELECT COUNT(*) duplicateGroups FROM (SELECT PageNumber,OriginalTestName,ValueText FROM LabResults WHERE MedicalReportId='{report_id}' GROUP BY PageNumber,OriginalTestName,ValueText HAVING COUNT(*)>1) d")[0]["duplicateGroups"]
            assert duplicate == 0
            return counts

        started = False
        try:
            log("Building and starting isolated phase13e API, AI service and SQL Server...")
            started = True
            dc("up", "-d", "--build", "--wait", "--wait-timeout", "180", "api", "ai-service", "sqlserver")
            assert request("GET", "/health/ready").json()["status"] == "Healthy"
            csrf()
            account_password = "SyntheticTest!" + secrets.token_hex(12)
            staff = request("POST", "/api/auth/register", json={"email": f"staff-{uuid.uuid4().hex}@phase13e.test", "password": account_password, "firstName": "Synthetic", "lastName": "Lab"}).json()
            patient = request("POST", "/api/auth/register", json={"email": f"patient-{uuid.uuid4().hex}@phase13e.test", "password": account_password, "firstName": "Synthetic", "lastName": "Patient"}).json()
            patient_id = patient["id"]
            org = str(uuid.uuid4())
            sql(f"INSERT INTO Organizations (Id,Name,Type,IsActive) VALUES ('{org}','Synthetic Phase13E Lab','LAB',1); UPDATE ApplicationUsers SET Role='LAB_STAFF',OrganizationId='{org}' WHERE Id='{staff['id']}'; INSERT INTO PatientAccessGrants (Id,PatientId,GrantedToOrganizationId,Scope,CreatedAt,GrantedByUserId) VALUES (NEWID(),'{patient_id}','{org}','REPORTS',SYSUTCDATETIME(),'{patient_id}');")
            request("POST", "/api/auth/login", json={"email": staff["email"], "password": account_password})
            csrf()
            fixtures = {kind: make_fixture(kind) for kind in ("native", "scanned", "hybrid")}
            assert all(len(data) < 20 * 1024 * 1024 for data in fixtures.values())
            run_id = uuid.uuid4().hex

            log("AI recovery: stop local AI, queue report, observe retry, restore before next attempt...")
            dc("stop", "ai-service")
            recovery = ingest("native", run_id + "-recovery")
            observed = []

            def retrying():
                state = status(recovery)
                if not observed or observed[-1] != [state["status"], state["attemptCount"]]:
                    observed.append([state["status"], state["attemptCount"]])
                assert state["status"] != "FAILED", state
                return state if state["status"] == "RETRYING" else None

            retry = wait_for(retrying)
            assert retry["safeErrorCode"] == "AI_SERVICE_UNAVAILABLE" and retry["attemptCount"] == 1
            retry_at = query_json(f"SELECT NextRetryAt FROM ProcessingJobs WHERE Id='{recovery['jobId']}'")[0]["NextRetryAt"]
            dc("start", "ai-service")
            final = terminal(recovery)
            assert final["status"] == "COMPLETED" and final["attemptCount"] == 2 and not final.get("safeErrorCode")
            assert datetime.fromisoformat(final["startedAt"]) >= datetime.fromisoformat(retry_at)
            counts = assert_single_set(recovery)
            assert counts["results"] == 3 and counts["issues"] == 0
            RESULTS["checks"]["aiRecovery"] = {"job": recovery, "observed": observed, "retry": retry, "nextRetryAt": retry_at, "final": final, "counts": counts}
            log("AI recovery passed: same job, attempt 2 COMPLETED, three results, no duplicates.")

            log("Authenticated Ollama-unreachable fallback...")
            before = report(recovery)
            explained = request("POST", f"/api/reports/{recovery['reportId']}/explanations/overview")
            body = explained.json()
            assert body["provider"] == "deterministic" and body["usedFallback"] is True
            assert len(body["validatedFindings"]) == 3
            after = report(recovery)
            assert before == after
            invalid = session.post(BASE + "/api/reports/not-a-guid/explanations/overview", timeout=10)
            assert 400 <= invalid.status_code < 500
            RESULTS["checks"]["ollamaFallback"] = {"httpStatus": explained.status_code, "provider": body["provider"], "usedFallback": body["usedFallback"], "validatedFindings": len(body["validatedFindings"]), "reportUnchanged": True, "invalidIdentifierStatus": invalid.status_code, "destination": "http://127.0.0.1:11434 (unreachable)"}

            version = dc("exec", "-T", "ai-service", "tesseract", "--version").splitlines()[0]
            log("Tesseract unavailable: execute scanned and native worker paths...")
            dc("exec", "-T", "ai-service", "mv", "/usr/bin/tesseract", "/usr/bin/tesseract.phase13e-disabled")
            try:
                scan = ingest("scanned", run_id + "-ocr-down")
                scan_state = terminal(scan)
                scan_report = report(scan)
                assert scan_state["status"] == "REVIEW_REQUIRED" and scan_state["safeErrorCode"] == "OCR_UNAVAILABLE"
                assert scan_report["ocrRequired"] is True and scan_report["ocrApplied"] is False and scan_report["resultsCount"] == 0
                native = ingest("native", run_id + "-native-no-ocr")
                native_state = terminal(native)
                native_report = report(native)
                assert native_state["status"] == "COMPLETED"
                assert native_report["ocrRequired"] is False and native_report["ocrApplied"] is False and native_report["resultsCount"] == 3
                RESULTS["checks"]["tesseractUnavailable"] = {"scanned": {"state": scan_state, "ocrRequired": True, "ocrApplied": False, "resultCount": 0, "reportStatus": scan_report["status"]}, "native": {"state": native_state, "ocrRequired": False, "ocrApplied": False, "resultCount": 3}}
            finally:
                dc("exec", "-T", "ai-service", "mv", "/usr/bin/tesseract.phase13e-disabled", "/usr/bin/tesseract")

            log("Benchmark: 4 native, 4 scanned, 2 hybrid authenticated uploads...")
            benchmark = []
            for index, kind in enumerate(["native"] * 4 + ["scanned"] * 4 + ["hybrid"] * 2):
                benchmark.append({**ingest(kind, f"{run_id}-bench-{index}"), "fixtureType": kind})
            peak_processing = peak_queued = 0
            deadline = time.monotonic() + 240
            while True:
                states = [status(job) for job in benchmark]
                # One server-side aggregate avoids combining job states from
                # different instants into an impossible concurrency snapshot.
                queue = request("GET", "/api/queue/health").json()
                processing = queue["processing"]
                queued = queue["queued"]
                peak_processing = max(peak_processing, processing)
                peak_queued = max(peak_queued, queued)
                if all(s["status"] in {"COMPLETED", "REVIEW_REQUIRED", "FAILED"} for s in states):
                    break
                assert time.monotonic() < deadline
                time.sleep(.2)
            ids = ",".join("'" + str(uuid.UUID(job["jobId"])) + "'" for job in benchmark)
            timing = query_json(f"SELECT Id jobId, ReportId reportId, QueuedAt queuedAt, StartedAt startedAt, CompletedAt completedAt, DATEDIFF_BIG(millisecond,QueuedAt,StartedAt) queueWaitMs, DATEDIFF_BIG(millisecond,StartedAt,CompletedAt) processingDurationMs, Status terminalStatus FROM ProcessingJobs WHERE Id IN ({ids})")
            kinds = {job["jobId"].lower(): job["fixtureType"] for job in benchmark}
            for row in timing:
                row["fixtureType"] = kinds[row["jobId"].lower()]
                data = report(row)
                row["processingMode"] = data["processingMode"]
                row["resultCount"] = data["resultsCount"]
                row["pageSources"] = data["pageSources"]
                row["reviewReasonCodes"] = sorted({issue["code"] for result in data["results"] for issue in result["validationIssues"]})
                assert row["terminalStatus"] in {"COMPLETED", "REVIEW_REQUIRED"}
                assert row["processingMode"] == {"native": "NATIVE", "scanned": "OCR", "hybrid": "HYBRID"}[row["fixtureType"]]
                assert row["resultCount"] >= 3
                assert_single_set(row)
                # Known synthetic values must survive the real worker/OCR path.
                for name, value in [("Hemoglobin", 10.8), ("Creatinine", .9), ("HbA1c", 6.8)]:
                    assert any((r.get("normalizedTestName") or r["originalTestName"]) == name and abs(float(r["valueNumeric"]) - value) < .001 for r in data["results"])
            host = {"os": platform.platform(), "cpu": platform.processor(), "tesseractVersion": version, "maxConcurrentJobs": 2}
            if os.name == "nt":
                inventory = json.loads(run(["powershell", "-NoProfile", "-Command", "$cpu=Get-CimInstance Win32_Processor; $computer=Get-CimInstance Win32_ComputerSystem; @{cpu=$cpu.Name; ramBytes=$computer.TotalPhysicalMemory; logicalProcessors=$computer.NumberOfLogicalProcessors} | ConvertTo-Json -Compress"]))
                host.update(inventory)
            aggregate = {"total": len(timing), "completed": sum(x["terminalStatus"] == "COMPLETED" for x in timing), "reviewRequired": sum(x["terminalStatus"] == "REVIEW_REQUIRED" for x in timing), "failed": sum(x["terminalStatus"] == "FAILED" for x in timing), "duplicates": 0, "averageQueueWaitMs": sum(x["queueWaitMs"] for x in timing) / len(timing), "averageProcessingDurationMs": sum(x["processingDurationMs"] for x in timing) / len(timing), "maxProcessingDurationMs": max(x["processingDurationMs"] for x in timing)}
            assert peak_processing <= 2
            performance = {"status": "EXECUTED", "executedAt": datetime.now(timezone.utc).isoformat(), "composeProject": PROJECT, "workflow": "authenticated LAB_STAFF HTTP ingestion, real Docker SQL/API/Tesseract", "host": host, "testOverrides": {"Processing:RetryBaseSeconds": 20}, "aggregate": aggregate, "observedQueue": {"peakProcessing": peak_processing, "peakQueued": peak_queued, "method": "Server-side queue-health aggregate snapshots; exact active-call instrumentation is separately covered by Phase13OperationalTests"}, "reports": timing}
            (ROOT / "PHASE13_PERFORMANCE_RESULTS.json").write_text(json.dumps(performance, indent=2) + "\n")
            RESULTS["checks"]["benchmark"] = aggregate
            log("Benchmark passed: " + json.dumps(aggregate))

            events = query_json("SELECT Action, COUNT(*) count FROM SecurityAuditEvents GROUP BY Action")
            actions = {row["Action"] for row in events}
            assert {"REPORT_QUEUED", "PROCESSING_STARTED", "PROCESSING_RETRY", "PROCESSING_COMPLETED"} <= actions
            # Inspect persisted metadata without writing its contents to the audit artifact.
            metadata = query_json("SELECT MetadataJson FROM SecurityAuditEvents")
            assert all(row.get("MetadataJson") in (None, "", "{}") for row in metadata)
            RESULTS["checks"]["audit"] = {"actions": events, "metadataRowsInspected": len(metadata), "metadataContainsSensitiveContent": False, "processingFailed": "not applicable: this pass restores before retry exhaustion; bounded terminal failure covered by worker regression"}
            RESULTS["status"] = "PASSED"
        except Exception as exc:
            RESULTS["status"] = "FAILED"
            RESULTS["error"] = str(exc)
            raise
        finally:
            (ROOT / "PHASE13E_OPERATIONAL_RESULTS.json").write_text(json.dumps(RESULTS, indent=2) + "\n")
            if started:
                log("Stopping isolated verification containers; persistent volumes retained.")
                dc("down")


if __name__ == "__main__":
    main()
