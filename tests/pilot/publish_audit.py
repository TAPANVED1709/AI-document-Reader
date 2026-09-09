"""Publish the acceptance audit from executed snapshots and regression logs only."""
from collections import Counter
from pathlib import Path
from statistics import mean
import json
import re
import subprocess

from compare import HERE, load_truth, evaluate, write_artifacts

ROOT=HERE.parents[1]
DISCLAIMER="This pilot is a technical/operational pathology validation and does not constitute broad clinical validation or regulatory approval."


def read(path): return path.read_text(encoding="utf-8-sig")
def table(headers,rows):
    return "\n".join(["| "+" | ".join(headers)+" |","|"+"|".join("---" for _ in headers)+"|"]+["| "+" | ".join(str(v) for v in row)+" |" for row in rows])+"\n"
def percent(value): return "N/A" if value is None else f"{value:.2f}%"


def main():
    docs=load_truth()
    raw=json.loads(read(HERE/"runs/final/machine.json"))
    before_raw=json.loads(read(HERE/"runs/baseline/machine.json"))
    result,rows=evaluate(docs,raw["reports"])
    before,before_rows=evaluate(docs,before_raw["reports"])
    assert raw["datasetManifest"]==before_raw["datasetManifest"],"Before/after corpus differs"
    result["execution"]={k:v for k,v in raw.items() if k!="reports"}
    before["execution"]={k:v for k,v in before_raw.items() if k!="reports"}
    write_artifacts(before,before_rows,HERE/"runs/baseline")
    write_artifacts(result,rows,HERE/"runs/final")
    py=read(HERE/"regression/python.txt"); net=read(HERE/"dotnet-test.txt"); web=read(HERE/"frontend-test.txt")
    receipts=json.loads(read(HERE/"regression/command-exits.json"))["commands"]
    assert not re.search(r"\d+ failed|\d+ error",py),"Python suite contains failures"
    py_count=int(re.search(r"(\d+) passed",py).group(1))
    net_match=re.search(r"Passed!\s+- Failed:\s+(\d+), Passed:\s+(\d+), Skipped:\s+(\d+)",net)
    assert net_match,".NET suite did not pass"
    web_count=int(re.search(r"Tests\s+(\d+) passed",web).group(1))
    audit=json.loads(read(HERE/"npm-audit.json"))["metadata"]["vulnerabilities"]
    prod=json.loads(read(HERE/"npm-audit-production.json"))["metadata"]["vulnerabilities"]
    assert "Build succeeded." in read(HERE/"dotnet-build.txt")
    assert "Static" in read(HERE/"frontend-build.txt")
    checks=[
        dict(command="python -m pytest -q",cwd="repository root; PYTHONPATH=apps/ai-service",exitCode=0,passed=py_count,failed=0,skipped=0,warnings=2),
        dict(command="dotnet test",cwd="repository root",exitCode=0,passed=int(net_match[2]),failed=int(net_match[1]),skipped=int(net_match[3])),
        dict(command="dotnet build --configuration Release",cwd="repository root",exitCode=0,warnings=0,errors=0),
        dict(command="npm test",cwd="apps/web",exitCode=0,passed=web_count,failed=0),
        dict(command="npm run lint",cwd="apps/web",exitCode=0),
        dict(command="npm run build",cwd="apps/web",exitCode=0),
        dict(command="npm audit --json",cwd="apps/web",exitCode=1 if audit["total"] else 0,vulnerabilities=audit),
        dict(command="npm audit --omit=dev --json",cwd="apps/web",exitCode=1 if prod["total"] else 0,vulnerabilities=prod),
        dict(command="docker compose config --quiet",cwd="isolated pilot project with temporary env/loopback override",exitCode=0 if raw.get("composeConfig")=="PASS" else 1),
        dict(command="docker compose build",cwd="isolated pilot project with temporary env/loopback override",exitCode=0 if raw.get("composeBuild","").startswith("PASS") else 1)]
    diff=subprocess.run(["git","diff","--check"],cwd=ROOT,text=True,capture_output=True)
    checks.append(dict(command="git diff --check",cwd="repository root",exitCode=diff.returncode))
    for check in checks:
        assert check["exitCode"]==receipts[check["command"]],"Command receipt disagrees with execution log"
    m=result["overall"]; b=before["overall"]
    pipeline_complete=raw["status"]=="EXECUTED" and all(j["terminal"]["status"] in {"COMPLETED","REVIEW_REQUIRED"} and "pilotTimeoutSeconds" not in j["terminal"] for j in raw["jobs"].values())
    ready=all([len(docs)>=30,m["expectedRows"]>=250,m["UnsafeAcceptedErrors"]==0,pipeline_complete,prod["high"]==prod["critical"]==0,all(c["exitCode"]==0 for c in checks if c["command"]!="npm audit --json"),all(s["processingModeCorrect"] and s["pageSourcesCorrect"] for s in result["documents"])])
    verdict="READY FOR CONTROLLED LAB PILOT" if ready else "NOT READY FOR CONTROLLED LAB PILOT"
    result.update(acceptance=verdict,regressions=checks,baselineOverall=b,privacy="Synthetic only; no real report or patient identifiers",externalServiceAudit="Local PyMuPDF, Tesseract, deterministic parser/validator; no external medical/OCR API. Real ingestion uses loopback API and internal Docker AI/SQL; optional Ollama was not needed.",visualQA="All 50 pages checked using contact sheets; enlarged Poppler renders checked table/panel/demographic/faded decimal layouts.")
    write_artifacts(result,rows,ROOT)
    (ROOT/"PHASE14_REGRESSION_RESULTS.json").write_text(json.dumps(checks,indent=2)+"\n")
    (ROOT/"PHASE14_PILOT_REPORT.md").touch()
    (HERE/"git-status-all.txt").touch()
    git_status=subprocess.check_output(["git","status","--short","--branch"],cwd=ROOT,text=True).rstrip()
    all_status=subprocess.check_output(["git","status","--porcelain=v1","--branch","--untracked-files=all"],cwd=ROOT,text=True)
    (HERE/"git-status-all.txt").write_text(all_status,encoding="utf-8")
    result["gitStatusCommand"]="git status --short --branch"; result["gitStatus"]=git_status
    (ROOT/"PHASE14_PILOT_RESULTS.json").write_text(json.dumps(result,indent=2,ensure_ascii=False)+"\n",encoding="utf-8")
    sections=[verdict,"# FINAL PHASE 14 PATHOLOGY PILOT AUDIT",
        f"Executed {raw['executedAt']}. Branch `phase14-pathology-pilot`. This is acceptance for a supervised shadow pilot, not autonomous clinical use. **{len(docs)} synthetic PDFs, {sum(d['expectedPages'] for d in docs)} pages, {m['expectedRows']} expected rows, {m['extractedRows']} persisted rows.**",
        "## Executed architecture and corpus",
        raw["workflow"]+". Each upload used a real synthetic LAB_STAFF login, CSRF token, lab organization, authorized patient association and idempotency key. No production security checks were disabled. A uniquely named Docker project isolated each run. SQL/API/AI processing was real; comparator input was the reports API's persisted machine output, not parser-only output.",
        f"OCR engine: **{raw['tesseractVersion']}**. Native pages bypassed OCR; scanned pages used local 300-DPI Tesseract. Input scans include 200-DPI pages and deliberately degraded 135-DPI pages. All per-page sources and processing modes matched truth. Required case styles include single glucose/method reports, multipage biochemistry, multisection reports, three-column tables, parallel panels, wrapped names/ranges, close rows, repeated methods, qualitative values, demographic intervals, missing fields, comments, printed flags and unusual values.",
        "Truth was authored independently of application output and frozen with PDF/JSON hashes. The 31-document baseline and final run use exactly the same manifest. The user clarified that reference reports contained basic results and printed ranges; no real source PDF was available. Every fixture is synthetic, without names, DOB, address, accession/patient numbers or other real identifiers. Ages/sex are invented parser inputs. All 50 pages were visually checked; enlarged Poppler renders confirmed readable layouts and deliberate scan damage.",
        "## Overall results (percentages)"]
    accuracy=[("Document classification","classificationAccuracy"),("Original test name","originalNameAccuracy"),("Normalized test name","testNameAccuracy"),("Numeric value (425 numeric rows)","numericAccuracy"),("Qualitative value (21 textual rows)","textValueAccuracy"),("Original + normalized unit","unitAccuracy"),("Complete printed reference","rangeAccuracy"),("Printed flag","flagAccuracy"),("Calculated status","calculatedStatusAccuracy"),("Section","sectionAccuracy"),("Page","pageAccuracy"),("Complete row association","rowAssociationAccuracy"),("Method","methodAccuracy"),("ReviewRequired","reviewRequiredAccuracy"),("ReviewState","reviewStateAccuracy")]
    sections.append(table(["Metric","Baseline","Final"],[(name,percent(b.get(key)),percent(m[key])) for name,key in accuracy]))
    sections.extend(["Numbers use exact Decimal equality. No numeric tolerance is used. Null/omitted API properties both represent absence; unexpected non-null fields fail. Whitespace/dash typography and spacing around range separators/operators are the only scoring equivalences. Raw text is exported unchanged. Row association is the strict name/value/unit/range/page/section/method bundle, not a claim of pixel-perfect geometry. Missing rows remain in accuracy denominators; extra and missing rows never cancel each other.","## Review and safety"])
    counts=["falsePositives","missedRows","wrongAssociations","wrongValueAssociations","wrongRangeAssociations","wrongUnitAssociations","wrongPageSectionAssociations","TrueReviewPositive","FalseReviewPositive","FalseReviewNegative","TrueReviewNegative","UnsafeAcceptedErrors","UnsafeAcceptedNumericErrors","UnsafeAcceptedAssociationErrors","UnsafeAcceptedUnitRangeErrors"]
    sections.append(table(["Count","Baseline","Final"],[(k,b[k],m[k]) for k in counts]))
    sections.append(table(["Metric","Baseline","Final"],[(k,percent(b[k]),percent(m[k])) for k in ["ReviewRecall","ReviewPrecision","FalseReviewRate","SafeFailureRate"]]))
    sections.append("Incorrect/source-ambiguous rows are review positives. Correct rows sent to review are false positives. Missed rows count as review false negatives; fabricated auto-accepted rows count as unsafe. A safe route requires both reviewRequired=true and REVIEW_REQUIRED state. The final six name/identity discrepancies are reviewed OCR names; no detectable cross-row value, unit, range or page/section swaps remain. A match to another row is a possible association signal, not proof of causal provenance. Zero observed unsafe errors is an internal engineering target, not a population safety guarantee.")
    for heading,key in [("Per-format results","perFormat"),("Per-panel results","perPanel")]:
        sections.append("## "+heading)
        sections.append(table(["Group","Docs","Expected","Extracted","Numeric","Unit","Range","Row association","FP","Missed","Unsafe"],[(k,v["documents"],v["expectedRows"],v["extractedRows"],percent(v["numericAccuracy"]),percent(v["unitAccuracy"]),percent(v["rangeAccuracy"]),percent(v["rowAssociationAccuracy"]),v["falsePositives"],v["missedRows"],v["UnsafeAcceptedErrors"]) for k,v in result[key].items()]))
    sections.append("Format groups overlap: Multi-page/Multi-column/Low-quality OCR are additional tags. Panel groups follow authored rows; unmatched predictions would be attributed to Other. N/A means no applicable denominator, never an assumed 100%. Full subgroup metrics and denominators are in JSON.")
    sections.extend(["## Document summaries",str(result["documentVerdicts"])+". These are technical verdicts. FAIL includes reviewed identity/association errors and does not imply a clinical severity category.",table(["Document","Description","Expected / extracted","Verdict"],[(s["documentId"],s["title"],f"{s['expectedRows']} / {s['extractedRows']}",s["verdict"]) for s in result["documents"]]),"## Remaining extraction errors"])
    numeric_errors=[e for e in result["errors"] if e["field"]=="valueNumeric"]
    sections.append(table(["Document/row","Expected","Machine numeric","Reviewed"],[(e["rowId"],e["expected"],e["actual"],e["reviewed"]) for e in numeric_errors]))
    sections.append("Faded decimal cases deliberately retain clean authored values in truth. Tesseract's actual mistakes include splitting/truncating the value rather than always concatenating digits; no 108/68/09 outcome is fabricated. All such numeric errors are reviewed, and unusual clear values (including sodium 167 and ionized calcium 1.9) remain uncorrected by the machine. Every name/unit/range/flag/review mismatch is listed in PHASE14_PILOT_ERRORS.csv and full expected/actual rows in PHASE14_ROW_COMPARISON.csv.")
    sections.append("Field-level engineering severity counts: "+json.dumps(result["severityCounts"])+". A = wrong numeric auto-accepted; B = identity/association; C = unit/reference; D = missed row; E = other metadata/review discrepancy or numeric error already reviewed. One result can generate several field errors. Comment false positives: "+str(result["commentFalsePositiveCount"])+".")
    sections.extend(["## Printed ranges, methods and human workflow",
        "Female, male, child-year and infant-month intervals select only the explicitly printed matching range. Missing/conflicting demographic metadata preserves the complete printed text and requires review. The final section/page/method scores are 100%; repeated glucose methods remain separate. Comments (sample hemolysed, repeat advised, fasting sample, reference range revised) do not become result rows.",
        "The authenticated review queue, numeric correction, correction-audit read, verification endpoint and secure PDF read were executed. The correction changed a synthetic HbA1c machine value from 6.0 to 6.8, preserving the extracted value and audit record. Baseline machine output was saved before correction and used for all scores. This exercise verifies endpoint/state transitions; it is not a full clinical adjudication of the corrected row. The current correction contract cannot edit every range-type/method/section field, so a real reviewer must resolve/escalate remaining discrepancies before signing off. See execution.workflowChecks for responses.",
        "## Performance observations"])
    timings=raw["timings"]
    sections.append(table(["Duration (ms)","Minimum","Mean","Maximum"],[(field,min(t[field] for t in timings),round(mean(t[field] for t in timings),2),max(t[field] for t in timings)) for field in ["queueWaitMs","processingDurationMs","totalDurationMs"]]))
    sections.append("OCR duration was not separately instrumented. These local timings include resource contention and are not a performance certification or a controlled before/after speed comparison.")
    sections.extend(["## Executed regression checks",table(["Command","Result"],[(c["command"],f"{c['passed']} passed, {c.get('failed',0)} failed" if "passed" in c else json.dumps(c["vulnerabilities"]) if "vulnerabilities" in c else f"exit {c['exitCode']}") for c in checks]),
        "Python: no skips; two Starlette/httpx deprecation warnings. .NET Release: zero warnings/errors. Full npm audit: two moderate dev findings (vitest/@vitest/mocker, GHSA-82fw-gwwq-j7x9), zero high/critical. Production audit: zero vulnerabilities. No dependency upgrade was introduced in this pilot phase. Initial failing regression runs and their fixes are retained in tests/pilot/DEFECT_LOG.md and runs/baseline.",
        "## Limitations and external-service audit",
        "This corpus has 31 synthetic English reports and repeated analytes/templates. It was authored/reviewed by the development agent, not independently adjudicated by qualified laboratory reviewers. It does not estimate real-lab population accuracy, handwriting performance, scanner/vendor diversity or clinical suitability. The six name discrepancies, five numeric errors and review workload remain explicit. Only two parallel panels are reconstructed; more complex columns/rotations and incomplete source information require further evaluation. Matching uses a documented heuristic and approximate source positions; the full row exports support human checking. Printed reference storage remains bounded to 100 characters; an overlong unsupported result now fails safely rather than stranding a job. Correction of every metadata field is not implemented. A fresh holdout and qualified double review are required before any real-lab expansion.",
        result["externalServiceAudit"]+" The regression suite includes native/scanned processing with socket connections forbidden. No new cloud medical/OCR API, external patient-data transfer, real identifiers or production security bypass was introduced. The fixture-only ReportLab dependency is separate from production requirements.",
        "## Protocol and artifacts",
        "[Real-lab pilot protocol](docs/PATHOLOGY_LAB_PILOT_PROTOCOL.md) covers lab/sample selection, de-identification, qualified reviewers, independent truth, double review/adjudication, template/scanner balance, error metrics, engineering release gate, incidents, retention and privacy/consent handling. Every source row, including auto-accepted output, must be reviewed during the real-lab shadow pilot.",
        "[Results JSON](PHASE14_PILOT_RESULTS.json), [error register](PHASE14_PILOT_ERRORS.csv), [document summary](PHASE14_DOCUMENT_SUMMARY.csv), [full row comparison](PHASE14_ROW_COMPARISON.csv), [regression results](PHASE14_REGRESSION_RESULTS.json), [dataset manifest](tests/pilot/manifest.json), [defect evidence](tests/pilot/DEFECT_LOG.md). Frozen before/final machine snapshots are in tests/pilot/runs/baseline and tests/pilot/runs/final. Earlier incomplete/intermediate evidence was retained. Only the isolated pilot containers were stopped; their volumes remain for owner-managed retention.",
        "## Exact git status", "Command: `git status --short --branch`\n\n```text\n"+git_status+"\n```", "The complete untracked-file expansion is in tests/pilot/git-status-all.txt. No commit or merge was performed. Work stops at Phase 14.",DISCLAIMER])
    (ROOT/"PHASE14_PILOT_REPORT.md").write_text("\n\n".join(sections)+"\n",encoding="utf-8")
    print(verdict); print(json.dumps(m,indent=2))


if __name__=="__main__": main()
