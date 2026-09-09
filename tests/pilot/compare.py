"""Independent, strict comparison of frozen truth to persisted machine output.

No application imports. Numbers use Decimal equality; nulls are assertions.
Matching uses name, page, method and source geometry, NEVER measured values/ranges.
Unmatched predictions and expectations are counted separately, not subtracted.
"""
from collections import Counter
from decimal import Decimal, InvalidOperation
from difflib import SequenceMatcher
from pathlib import Path
import csv
import hashlib
import json
import re

HERE=Path(__file__).resolve().parent
FIELDS=["pageNumber","sectionName","originalTestName","normalizedTestName","valueNumeric","valueText",
        "valueOperator","originalUnit","normalizedUnit","unit","referenceText","referenceType",
        "referenceMin","referenceMax","referenceOperator","reportedFlag","calculatedStatus","methodText",
        "reviewRequired","reviewState"]
NUMBERS={"valueNumeric","referenceMin","referenceMax"}
REVIEW={"reviewRequired","reviewState"}
RANGES={"referenceText","referenceType","referenceMin","referenceMax","referenceOperator"}
UNITS={"unit","originalUnit","normalizedUnit"}
ASSOCIATION={"pageNumber","sectionName","originalTestName","normalizedTestName","methodText"}
FORMATS=["Native","Scanned","Hybrid","Multi-page","Multi-column","Low-quality OCR"]
PANELS=["CBC","Liver","Kidney","Diabetes","Lipids","Thyroid","Iron","Vitamins","Inflammation","Coagulation","Urine","Electrolytes","Serology / Immunology","Other"]


def normalized(value):
    # Only whitespace/dash typography. No case-folding, OCR repairs or numeric tolerances.
    return " ".join(value.translate(str.maketrans({"–":"-","—":"-","−":"-"})).split()) if isinstance(value,str) else value


def equal(field, expected, actual):
    if expected is None or actual is None: return expected is None and actual is None
    if field in NUMBERS:
        try: return Decimal(str(expected)).is_finite() and Decimal(str(expected)) == Decimal(str(actual))
        except InvalidOperation: return False
    if field=="referenceText":
        def range_spacing(text):
            text=normalized(text)
            text=re.sub(r"(?<=\d)\s*-\s*(?=[+-]?\d)"," - ",text)
            return re.sub(r"([<>]=?)\s*",r"\1 ",text)
        return range_spacing(expected)==range_spacing(actual)
    return normalized(expected)==normalized(actual)


def load_truth(folder=HERE):
    manifest=json.loads((folder/"manifest.json").read_text())
    docs=[]; ids=set()
    for m in manifest:
        key=m["documentId"]
        if key in ids: raise ValueError("Duplicate document ID")
        ids.add(key)
        data=(folder/"ground_truth"/f"{key}.json").read_bytes()
        pdf=(folder/"fixtures"/f"{key}.pdf").read_bytes()
        if hashlib.sha256(data).hexdigest()!=m["truthSha256"] or hashlib.sha256(pdf).hexdigest()!=m["pdfSha256"]:
            raise ValueError(f"Frozen dataset hash mismatch: {key}")
        d=json.loads(data); row_ids=set()
        if d["documentId"]!=key or len(d["expectedResults"])!=m["rows"]: raise ValueError("Manifest mismatch")
        for r in d["expectedResults"]:
            if not set(FIELDS)<=r.keys() or r["rowId"] in row_ids: raise ValueError("Incomplete or duplicate truth row")
            row_ids.add(r["rowId"])
            if not 1<=r["pageNumber"]<=d["expectedPages"] or r["panel"] not in PANELS: raise ValueError("Invalid row page/panel")
            for f in NUMBERS:
                if r[f] is not None and not Decimal(str(r[f])).is_finite(): raise ValueError("Invalid numeric truth")
            if r["reviewState"] != ("REVIEW_REQUIRED" if r["reviewRequired"] else "AUTO_ACCEPTED"): raise ValueError("Inconsistent truth review state")
        docs.append(d)
    return docs


def match_rows(expected, actual):
    candidates=[]
    for ei,e in enumerate(expected):
        for ai,a in enumerate(actual):
            names=[SequenceMatcher(None,str(e[f]).lower(),str(a.get(g) or "").lower()).ratio()
                   for f in ("originalTestName","normalizedTestName") for g in ("originalTestName","normalizedTestName")]
            sim=max(names)
            if sim<.55: continue
            exact=any(equal(f,e[f],a.get(f)) for f in ("originalTestName","normalizedTestName"))
            score=(10 if exact else 0)+sim*3+(e["pageNumber"]==a.get("pageNumber"))*2
            if e["methodText"] is not None: score+=(e["methodText"]==a.get("methodText"))*2
            # Helps repeated same-name/method rows without looking at their measured data.
            if a.get("boundingBoxJson") and e.get("sourceRegion"):
                try:
                    box=json.loads(a["boundingBoxJson"]); scale=72/box.get("dpi",72) if box.get("coordinateSpace")=="rendered_pixels" else 1
                    region=e["sourceRegion"]
                    score+=max(0,1-abs(box["y"]*scale-region["y"])/100)
                    score+=max(0,1-abs(box["x"]*scale-region["x"])/100)
                except (KeyError,TypeError,ValueError): pass
            candidates.append((score,ei,ai))
    used_e=set(); used_a=set(); pairs={}
    for score,ei,ai in sorted(candidates,reverse=True):
        if ei not in used_e and ai not in used_a:
            pairs[ei]=ai; used_e.add(ei); used_a.add(ai)
    return pairs,sorted(set(range(len(actual)))-used_a)


def compare_document(d, report):
    expected=d["expectedResults"]; actual=report.get("results",[])
    pairs,extras=match_rows(expected,actual); output=[]; errors=[]
    document_ok=report.get("documentType")==d["expectedDocumentType"]
    page_sources=[p.get("source") for p in sorted(report.get("pageSources") or [],key=lambda p:p["page"])]
    mode_ok=report.get("processingMode")==d["expectedProcessingMode"]
    sources_ok=page_sources==d["expectedPageSources"]
    def error(record,field,want,got,severity,kind):
        errors.append(dict(documentId=d["documentId"],rowId=record["rowId"],actualId=record["actualId"],panel=record["panel"],field=field,expected=want,actual=got,severity=severity,kind=kind,reviewed=record["reviewed"],unsafe=record["unsafe"]))
    for ei,e in enumerate(expected):
        a=actual[pairs[ei]] if ei in pairs else None
        # ASP.NET omits nullable properties when null. Omitted and explicit null
        # both mean absent; a non-null expectation still fails when omitted.
        checks={f:a is not None and equal(f,e[f],a.get(f)) for f in FIELDS}
        mismatches={f for f,ok in checks.items() if not ok}
        content=mismatches-REVIEW
        reviewed=bool(a and a.get("reviewRequired") is True and a.get("reviewState")=="REVIEW_REQUIRED")
        # A contradictory state (review=true but AUTO_ACCEPTED) does not satisfy the gate.
        incorrect=bool(content) or not document_ok or bool(e["reviewRequired"])
        unsafe=a is not None and incorrect and not reviewed
        wrong_association=bool(a and content & ASSOCIATION)
        swapped_value=bool(a and "valueNumeric" in content and any(other is not e and other["pageNumber"]==e["pageNumber"] and other["valueNumeric"]!=e["valueNumeric"] and equal("valueNumeric",other["valueNumeric"],a.get("valueNumeric")) for other in expected))
        swapped_range=bool(a and content & RANGES and any(other is not e and other["pageNumber"]==e["pageNumber"] and other["referenceText"] is not None and all(equal(f,other[f],a.get(f)) for f in RANGES) for other in expected))
        swapped_unit=bool(a and content & UNITS and any(other is not e and other["pageNumber"]==e["pageNumber"] and other["normalizedUnit"] is not None and all(equal(f,other[f],a.get(f)) for f in UNITS) for other in expected))
        wrong_association |= swapped_value or swapped_range or swapped_unit
        r=dict(documentId=d["documentId"],rowId=e["rowId"],actualId=a.get("id") if a else None,panel=e["panel"],tags=d["tags"],matched=a is not None,extra=False,
               checks=checks,expected=e,actual=a,reviewed=reviewed,incorrect=incorrect,unsafe=unsafe,
               unsafeNumeric=unsafe and "valueNumeric" in content,unsafeAssociation=unsafe and wrong_association,
               unsafeUnitRange=unsafe and bool(content & (UNITS|RANGES)),wrongAssociation=wrong_association,
               wrongValueAssociation=swapped_value,wrongRangeAssociation=swapped_range,wrongUnitAssociation=swapped_unit,wrongPageSection=bool(a and content & {"pageNumber","sectionName"}),
               rowAssociation=a is not None and not bool(content & (ASSOCIATION|UNITS|RANGES|{"valueNumeric","valueText"})))
        output.append(r)
        if a is None: error(r,"row",e["originalTestName"],None,"D","MISSED_ROW"); continue
        for f in sorted(mismatches):
            severity="A" if f=="valueNumeric" and unsafe else "B" if f in ASSOCIATION or f=="valueNumeric" and swapped_value else "C" if f in UNITS|RANGES else "E"
            error(r,f,e[f],a.get(f),severity,"WRONG_ASSOCIATION" if severity=="B" else "FIELD_MISMATCH")
        if not document_ok: error(r,"documentType",d["expectedDocumentType"],report.get("documentType"),"E","CLASSIFICATION")
    for ai in extras:
        a=actual[ai]; reviewed=a.get("reviewRequired") is True and a.get("reviewState")=="REVIEW_REQUIRED"
        r=dict(documentId=d["documentId"],rowId=f"extra-{ai}",actualId=a.get("id"),panel="Other",tags=d["tags"],matched=False,extra=True,checks={},expected=None,actual=a,reviewed=reviewed,incorrect=True,unsafe=not reviewed,unsafeNumeric=False,unsafeAssociation=not reviewed,unsafeUnitRange=False,wrongAssociation=True,rowAssociation=False,wrongValueAssociation=False,wrongRangeAssociation=False,wrongUnitAssociation=False,wrongPageSection=False)
        output.append(r); error(r,"row",None,a.get("originalTestName"),"B","FALSE_POSITIVE")
    missing=sum(not r["matched"] and not r["extra"] for r in output)
    unsafe_count=sum(r["unsafe"] for r in output)
    # Correct-but-unnecessarily-reviewed rows remain REVIEW and reduce review precision.
    verdict="FAIL" if unsafe_count or missing or extras or any(r["wrongAssociation"] for r in output) or not document_ok or not mode_ok or not sources_ok else "REVIEW" if any(r["reviewed"] or r["incorrect"] for r in output) else "PASS"
    summary=dict(documentId=d["documentId"],title=d["title"],format=d["format"],tags=d["tags"],expectedRows=len(expected),extractedRows=len(actual),classificationCorrect=document_ok,
                 processingMode=report.get("processingMode"),expectedProcessingMode=d["expectedProcessingMode"],pageSources=report.get("pageSources"),processingModeCorrect=mode_ok,pageSourcesCorrect=sources_ok,verdict=verdict,
                 falsePositives=len(extras),missedRows=missing,unsafeAcceptedErrors=unsafe_count,wrongAssociations=sum(r["wrongAssociation"] for r in output),fieldMismatches=len(errors))
    return output,errors,summary


def rate(n,d): return round(100*n/d,4) if d else None


def metrics(rows, summaries):
    expected=[r for r in rows if not r["extra"]]
    def accuracy(fields,selected=expected): return rate(sum(all(r["checks"].get(f,False) for f in fields) for r in selected),len(selected))
    tp=sum(r["incorrect"] and r["reviewed"] for r in rows); fp=sum(not r["incorrect"] and r["reviewed"] for r in rows)
    fn=sum(r["incorrect"] and not r["reviewed"] for r in rows); tn=sum(not r["incorrect"] and not r["reviewed"] for r in rows)
    return dict(documents=len(summaries),expectedRows=len(expected),extractedRows=sum(r["matched"] or r["extra"] for r in rows),
                classificationAccuracy=rate(sum(s["classificationCorrect"] for s in summaries),len(summaries)),
                originalNameAccuracy=accuracy(["originalTestName"]),testNameAccuracy=accuracy(["normalizedTestName"]),
                numericAccuracy=accuracy(["valueNumeric"],[r for r in expected if r["expected"]["valueNumeric"] is not None]),
                numericExpectedRows=sum(r["expected"]["valueNumeric"] is not None for r in expected),
                textValueAccuracy=accuracy(["valueText","valueNumeric"],[r for r in expected if r["expected"]["valueNumeric"] is None]),
                textExpectedRows=sum(r["expected"]["valueNumeric"] is None for r in expected),
                unitAccuracy=accuracy(UNITS),rangeAccuracy=accuracy(RANGES),flagAccuracy=accuracy(["reportedFlag"]),sectionAccuracy=accuracy(["sectionName"]),pageAccuracy=accuracy(["pageNumber"]),
                calculatedStatusAccuracy=accuracy(["calculatedStatus"]),rawRangeAccuracy=accuracy(["referenceText"]),
                rowAssociationAccuracy=rate(sum(r["rowAssociation"] for r in expected),len(expected)),methodAccuracy=accuracy(["methodText"]),
                reviewRequiredAccuracy=accuracy(["reviewRequired"]),reviewStateAccuracy=accuracy(["reviewState"]),
                falsePositives=sum(r["extra"] for r in rows),missedRows=sum(not r["matched"] for r in expected),wrongAssociations=sum(r["wrongAssociation"] for r in rows),
                wrongValueAssociations=sum(r["wrongValueAssociation"] for r in rows),wrongRangeAssociations=sum(r["wrongRangeAssociation"] for r in rows),wrongUnitAssociations=sum(r["wrongUnitAssociation"] for r in rows),wrongPageSectionAssociations=sum(r["wrongPageSection"] for r in rows),
                TrueReviewPositive=tp,FalseReviewPositive=fp,FalseReviewNegative=fn,TrueReviewNegative=tn,
                ReviewRecall=rate(tp,tp+fn),ReviewPrecision=rate(tp,tp+fp),FalseReviewRate=rate(fp,fp+tn),SafeFailureRate=rate(tp,tp+fn),
                UnsafeAcceptedErrors=sum(r["unsafe"] for r in rows),UnsafeAcceptedNumericErrors=sum(r["unsafeNumeric"] for r in rows),
                UnsafeAcceptedAssociationErrors=sum(r["unsafeAssociation"] for r in rows),UnsafeAcceptedUnitRangeErrors=sum(r["unsafeUnitRange"] for r in rows))


def evaluate(docs,reports):
    rows=[]; errors=[]; summaries=[]
    for d in docs:
        rr,ee,ss=compare_document(d,reports.get(d["documentId"],{})); rows+=rr; errors+=ee; summaries.append(ss)
    overall=metrics(rows,summaries)
    formats={f:metrics([r for r in rows if f in r["tags"]],[s for s in summaries if f in s["tags"]]) for f in FORMATS}
    panels={}
    for p in PANELS:
        selected=[r for r in rows if r["panel"]==p]; ids={r["documentId"] for r in selected}
        panels[p]=metrics(selected,[s for s in summaries if s["documentId"] in ids])
    result=dict(overall=overall,perFormat=formats,perPanel=panels,documentVerdicts=dict(Counter(s["verdict"] for s in summaries)),documents=summaries,
                severityCounts={s:sum(e["severity"]==s for e in errors) for s in "ABCDE"},errors=errors,
                commentFalsePositiveCount=sum(r["extra"] and any(t in str(r["actual"]).lower() for t in ["hemolysed","repeat advised","fasting sample","range revised"]) for r in rows))
    return result,rows


def write_artifacts(result,rows,folder):
    folder.mkdir(parents=True,exist_ok=True)
    (folder/"PHASE14_PILOT_RESULTS.json").write_text(json.dumps(result,indent=2,ensure_ascii=False)+"\n",encoding="utf-8")
    def csvfile(name,items,fields):
        with (folder/name).open("w",encoding="utf-8",newline="") as f:
            writer=csv.DictWriter(f,fieldnames=fields,extrasaction="ignore"); writer.writeheader()
            for item in items: writer.writerow({k:json.dumps(v,ensure_ascii=False) if isinstance(v,(list,dict)) else v for k,v in item.items()})
    csvfile("PHASE14_PILOT_ERRORS.csv",result["errors"],["documentId","rowId","actualId","panel","kind","severity","field","expected","actual","reviewed","unsafe"])
    csvfile("PHASE14_DOCUMENT_SUMMARY.csv",result["documents"],["documentId","title","format","tags","expectedRows","extractedRows","classificationCorrect","processingMode","verdict","falsePositives","missedRows","unsafeAcceptedErrors","wrongAssociations","fieldMismatches"])
    csvfile("PHASE14_ROW_COMPARISON.csv",rows,["documentId","rowId","actualId","panel","matched","extra","reviewed","incorrect","unsafe","rowAssociation","checks","expected","actual"])


if __name__=="__main__":
    import argparse
    p=argparse.ArgumentParser(); p.add_argument("run",type=Path); args=p.parse_args()
    raw=json.loads((args.run/"machine.json").read_text(encoding="utf-8")); result,rows=evaluate(load_truth(),raw["reports"])
    result["execution"]= {k:v for k,v in raw.items() if k!="reports"}
    write_artifacts(result,rows,args.run)
    print(json.dumps(result["overall"],indent=2))
