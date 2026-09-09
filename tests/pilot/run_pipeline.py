"""Run all frozen fixtures through authenticated local Docker ingestion/persistence.

python tests/pilot/run_pipeline.py --label before
python tests/pilot/run_pipeline.py --label after
Run labels are immutable: choose a new label instead of overwriting evidence.
Test credentials are ephemeral; only this uniquely named Compose project is stopped.
"""
from datetime import datetime, timezone
from pathlib import Path
import argparse
import json
import secrets
import subprocess
import tempfile
import time
import uuid

import httpx
from compare import HERE, load_truth, evaluate, write_artifacts

ROOT=HERE.parents[1]
BASE="http://127.0.0.1:18080"


def main(label, job_timeout=240, baseline_images=None, full_build=False):
    folder=HERE/"runs"/label
    folder.mkdir(parents=True,exist_ok=False)
    docs=load_truth(); project="phase14-"+secrets.token_hex(4)
    raw=dict(executedAt=datetime.now(timezone.utc).isoformat(),composeProject=project,workflow="Authenticated LAB_STAFF HTTP ingestion -> persisted ProcessingJob -> worker -> local AI -> native/Tesseract -> parser -> validation -> SQL -> reports API",reports={},jobs={},timings=[],workflowChecks={},ocrDuration="Not separately instrumented; processing duration includes OCR",sourceRevision=subprocess.check_output(["git","rev-parse","HEAD"],cwd=ROOT,text=True).strip())
    raw["datasetManifest"]=json.loads((HERE/"manifest.json").read_text())
    raw["baselineImageProject"]=baseline_images
    def save(): (folder/"machine.json").write_text(json.dumps(raw,indent=2,ensure_ascii=False)+"\n",encoding="utf-8")
    def run(args):
        p=subprocess.run(args,cwd=ROOT,text=True,capture_output=True,timeout=1200,encoding="utf-8",errors="replace")
        if p.returncode: raise RuntimeError(f"{args[0]} failed: {p.stderr[-2000:]}")
        return p.stdout
    with tempfile.TemporaryDirectory(prefix="phase14-") as temporary, httpx.Client(trust_env=False,timeout=120) as client:
        temp=Path(temporary); password="Synthetic!"+secrets.token_hex(16)
        env=temp/"compose.env"; env.write_text(f"MSSQL_SA_PASSWORD={password}\nConnectionStrings__DefaultConnection=Server=sqlserver,1433;Database=AiDocumentReaderDb;User Id=sa;Password={password};TrustServerCertificate=True;\n")
        override=temp/"override.yml"; override.write_text('''services:
  api:
    ports: ["127.0.0.1:18080:8080"]
    environment:
      Logging__LogLevel__Microsoft.EntityFrameworkCore: Warning
  ai-service:
    environment:
      LOCAL_LLM_MODEL: ""
''')
        if baseline_images:
            override.write_text(override.read_text().replace('  api:\n','  api:\n    image: '+baseline_images+'-api\n').replace('  ai-service:\n','  ai-service:\n    image: '+baseline_images+'-ai-service\n'))
        compose=["docker","compose","-p",project,"--env-file",str(env),"-f",str(ROOT/"docker-compose.yml"),"-f",str(override)]
        def dc(*args): return run(compose+list(args))
        def sql(query):
            script=temp/"query.sql"; script.write_text("SET NOCOUNT ON;\n"+query,encoding="utf-8")
            run(["docker","cp",str(script),f"{project}-sqlserver-1:/tmp/phase14.sql"])
            return dc("exec","-T","sqlserver","sh","-c",'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d AiDocumentReaderDb -w 65535 -y 0 -i /tmp/phase14.sql')
        def query_json(query):
            txt="".join(sql(query+" FOR JSON PATH").splitlines())
            return json.loads(txt[txt.index("["):txt.rindex("]")+1])
        def request(method,path,**kw):
            response=client.request(method,BASE+path,**kw)
            response.raise_for_status(); return response
        def csrf(): client.headers["X-XSRF-TOKEN"]=request("GET","/api/auth/csrf").json()["token"]
        try:
            print(f"Starting isolated {project} production services",flush=True)
            dc("config","--quiet"); raw["composeConfig"]="PASS"
            if full_build:
                print("Executing docker compose build for all production services",flush=True)
                (folder/"docker-build.txt").write_text(dc("build"),encoding="utf-8")
                raw["composeBuild"]="PASS (all production services)"
            dc("up","-d","--no-build" if baseline_images else "--build","--wait","--wait-timeout","180","api","ai-service","sqlserver")
            raw["images"]={s:run(["docker","inspect",f"{project}-{s}-1","--format","{{.Image}}"] ).strip() for s in ["api","ai-service"]}
            raw["tesseractVersion"]=dc("exec","-T","ai-service","tesseract","--version").splitlines()[0]
            csrf(); account_password="SyntheticTest!"+secrets.token_hex(12)
            staff=request("POST","/api/auth/register",json={"email":f"staff-{uuid.uuid4().hex}@pilot.test","password":account_password,"firstName":"Synthetic","lastName":"Staff"}).json()
            patient=request("POST","/api/auth/register",json={"email":f"subject-{uuid.uuid4().hex}@pilot.test","password":account_password,"firstName":"Synthetic","lastName":"Subject"}).json()
            org=str(uuid.uuid4())
            sql(f"INSERT INTO Organizations (Id,Name,Type,IsActive) VALUES ('{org}','Synthetic Engineering Pilot','LAB',1); UPDATE ApplicationUsers SET Role='LAB_STAFF',OrganizationId='{org}' WHERE Id='{staff['id']}'; INSERT INTO PatientAccessGrants (Id,PatientId,GrantedToOrganizationId,Scope,CreatedAt,GrantedByUserId) VALUES (NEWID(),'{patient['id']}','{org}','REPORTS',SYSUTCDATETIME(),'{patient['id']}');")
            request("POST","/api/auth/login",json={"email":staff["email"],"password":account_password}); csrf()
            for d in docs:
                key=d["documentId"]
                job=request("POST","/api/lab-ingestion/reports",headers={"Idempotency-Key":project+"-"+key},data={"patientId":patient["id"]},files={"file":(key+".pdf",(HERE/"fixtures"/f"{key}.pdf").read_bytes(),"application/pdf")}).json()
                raw["jobs"][key]=job
                deadline=time.monotonic()+job_timeout
                while True:
                    state=request("GET",f"/api/reports/{job['reportId']}/processing-status").json()
                    if state["status"] in {"COMPLETED","REVIEW_REQUIRED","FAILED"}: break
                    if time.monotonic()>deadline:
                        state["pilotTimeoutSeconds"]=job_timeout
                        break  # A stuck job is a measured failure; continue the full corpus.
                    time.sleep(.4)
                raw["reports"][key]=request("GET",f"/api/reports/{job['reportId']}").json()
                raw["jobs"][key]["terminal"]=state
                print(f"{key}: {state['status']}, {raw['reports'][key]['resultsCount']} persisted rows",flush=True); save()
            raw["timings"]=query_json("SELECT Id jobId,ReportId reportId,DATEDIFF_BIG(millisecond,QueuedAt,StartedAt) queueWaitMs,DATEDIFF_BIG(millisecond,StartedAt,CompletedAt) processingDurationMs,DATEDIFF_BIG(millisecond,QueuedAt,CompletedAt) totalDurationMs,Status terminalStatus FROM ProcessingJobs")
            result,rows=evaluate(docs,raw["reports"])
            write_artifacts(result,rows,folder)
            queue=request("GET","/api/review-queue").json()
            raw["workflowChecks"]["reviewQueue"]={"httpStatus":200,"reports":len(queue),"reportIds":[x["reportId"] for x in queue]}
            candidate=next((r for r in rows if r["matched"] and not r["checks"]["valueNumeric"] and r["expected"]["valueNumeric"] is not None),None)
            if candidate:
                rid=raw["jobs"][candidate["documentId"]]["reportId"]; aid=candidate["actualId"]
                value=float(candidate["expected"]["valueNumeric"])
                corrected=request("PATCH",f"/api/reports/{rid}/results/{aid}",json={"value":value,"valueText":candidate["expected"]["valueText"],"reason":"Synthetic pilot adjudication; frozen machine mismatch retained"}).json()
                audit=request("GET",f"/api/reports/{rid}/results/{aid}/audit").json()
                verified=request("POST",f"/api/reports/{rid}/results/{aid}/verify").json()
                raw["workflowChecks"]["correctionVerification"]={"documentId":candidate["documentId"],"rowId":candidate["rowId"],"before":candidate["actual"],"correction":corrected,"audit":audit,"verification":verified,"baselinePreserved":True}
                assert audit and verified["isVerified"]
            else:
                raw["workflowChecks"]["correctionVerification"]={"status":"No numeric mismatch to correct; verify a reviewed row only"}
                target=next((r for r in rows if r["matched"] and r["reviewed"]),None)
                if target:
                    rid=raw["jobs"][target["documentId"]]["reportId"]
                    raw["workflowChecks"]["verification"]=request("POST",f"/api/reports/{rid}/results/{target['actualId']}/verify").json()
            first=next(iter(raw["jobs"].values()))["reportId"]
            raw["workflowChecks"]["securePdfRead"]=request("GET",f"/api/reports/{first}/file").status_code
            raw["workflowChecks"]["auditActions"]=query_json("SELECT Action,COUNT(*) count FROM SecurityAuditEvents GROUP BY Action")
            raw["status"]="EXECUTED"; save()
            result["execution"]={k:v for k,v in raw.items() if k!="reports"}
            write_artifacts(result,rows,folder)
            print(json.dumps(result["overall"],indent=2),flush=True)
        except Exception as exc:
            raw["status"]="HARNESS_FAILED"; raw["error"]=str(exc); save(); raise
        finally:
            logs=dc("logs","--no-color","--tail","100","api")
            logs=logs.replace(password,"[REDACTED]")
            if "account_password" in locals(): logs=logs.replace(account_password,"[REDACTED]")
            (folder/"api-tail.txt").write_text(logs,encoding="utf-8")
            print("Stopping this isolated pilot project; volumes retained",flush=True)
            dc("down")


if __name__=="__main__":
    p=argparse.ArgumentParser(); p.add_argument("--label",required=True); p.add_argument("--job-timeout",type=int,default=240); p.add_argument("--baseline-images"); p.add_argument("--full-build",action="store_true"); args=p.parse_args()
    if not args.label.replace("-","").isalnum(): p.error("Use a simple alphanumeric run label")
    main(args.label,args.job_timeout,args.baseline_images,args.full_build)
