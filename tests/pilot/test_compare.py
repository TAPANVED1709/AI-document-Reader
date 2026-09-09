from copy import deepcopy
from pathlib import Path
import json
import sys

import pytest

sys.path.insert(0,str(Path(__file__).resolve().parent))
from compare import load_truth, equal, compare_document, evaluate, metrics, FIELDS


@pytest.fixture
def sample():
    d=deepcopy(load_truth()[4]); d["expectedResults"]=d["expectedResults"][:3]
    a={"documentType":"LAB_REPORT","processingMode":"NATIVE","pageSources":[{"page":i+1,"source":s} for i,s in enumerate(d["expectedPageSources"])],"results":[{**r,"id":f"actual-{i}"} for i,r in enumerate(d["expectedResults"])]}
    return d,a


def test_dataset_hashes_schema_and_coverage():
    docs=load_truth()
    assert len(docs)>=30 and sum(len(d["expectedResults"]) for d in docs)>=250
    assert {"Native","Scanned","Hybrid","Multi-page","Multi-column","Low-quality OCR"} <= {t for d in docs for t in d["tags"]}
    assert all(set(FIELDS)<=r.keys() for d in docs for r in d["expectedResults"])


def test_loader_rejects_modified_truth(tmp_path):
    import shutil
    src=Path(__file__).resolve().parent
    m=json.loads((src/"manifest.json").read_text())[:1]
    (tmp_path/"manifest.json").write_text(json.dumps(m))
    for folder,extension in [("ground_truth","json"),("fixtures","pdf")]:
        (tmp_path/folder).mkdir()
        shutil.copyfile(src/folder/f'pilot-01.{extension}',tmp_path/folder/f'pilot-01.{extension}')
    with (tmp_path/"ground_truth/pilot-01.json").open("a") as f: f.write(" ")
    with pytest.raises(ValueError,match="hash mismatch"): load_truth(tmp_path)


def test_exact_numeric_and_null_comparison():
    assert equal("valueNumeric","10.80",10.8)
    assert not equal("valueNumeric","10.8",108)
    assert not equal("valueNumeric","10.8",10.80001)
    assert not equal("referenceMin",None,0)
    assert not equal("valueText","Non-Reactive","Reactive")
    assert not equal("valueNumeric","NaN","NaN")
    assert equal("referenceText","4.0 - 11.0","4.0- 11.0")
    assert equal("referenceText","< 5","<5")
    assert not equal("referenceText","4.0 - 11.0","40 - 11.0")
    assert not equal("referenceText","<5","<=5")


def test_api_omitted_nulls_are_absence_not_skipped_assertions(sample):
    d,a=sample
    a["results"]=[{k:v for k,v in r.items() if v is not None} for r in a["results"]]
    _,errors,_=compare_document(d,a)
    assert not errors
    a["results"][0]["referenceOperator"]="<"
    _,errors,_=compare_document(d,a)
    assert any(e["field"]=="referenceOperator" for e in errors)


def test_complete_match_is_pass(sample):
    d,a=sample; rows,errors,s=compare_document(d,a)
    assert s["verdict"]=="PASS" and not errors and all(r["rowAssociation"] for r in rows)


def test_missing_rows_reduce_all_applicable_accuracy(sample):
    d,a=sample; a["results"].pop(0)
    result,_=evaluate([d],{d["documentId"]:a}); m=result["overall"]
    assert m["missedRows"]==1 and m["numericAccuracy"]==66.6667
    assert m["FalseReviewNegative"]==1 and result["documents"][0]["verdict"]=="FAIL"
    assert result["errors"][0]["severity"]=="D"


def test_extra_and_missing_do_not_cancel(sample):
    d,a=sample; a["results"].pop(0)
    a["results"].append({**a["results"][0],"id":"fake","originalTestName":"Sample hemolysed","normalizedTestName":"Sample hemolysed"})
    result,_=evaluate([d],{d["documentId"]:a})
    assert result["overall"]["missedRows"]==result["overall"]["falsePositives"]==1
    assert result["commentFalsePositiveCount"]==1
    assert result["overall"]["UnsafeAcceptedErrors"]==1


def test_numeric_unsafe_and_review_confusion(sample):
    d,a=sample; a["results"][0]["valueNumeric"]=920
    result,_=evaluate([d],{d["documentId"]:a}); m=result["overall"]
    assert m["UnsafeAcceptedErrors"]==m["UnsafeAcceptedNumericErrors"]==1
    assert result["errors"][0]["severity"]=="A"
    a["results"][0].update(reviewRequired=True,reviewState="REVIEW_REQUIRED")
    a["results"][1].update(reviewRequired=True,reviewState="REVIEW_REQUIRED")
    result,_=evaluate([d],{d["documentId"]:a}); m=result["overall"]
    assert m["UnsafeAcceptedErrors"]==0 and m["TrueReviewPositive"]==1 and m["FalseReviewPositive"]==1
    assert m["ReviewPrecision"]==50 and m["ReviewRecall"]==100 and m["FalseReviewRate"]==50


def test_false_review_flag_with_auto_state_is_unsafe(sample):
    d,a=sample; a["results"][0].update(valueNumeric=920,reviewRequired=True)
    result,_=evaluate([d],{d["documentId"]:a})
    assert result["overall"]["UnsafeAcceptedErrors"]==1


def test_unit_range_absence_is_asserted_and_severity_c(sample):
    d,a=sample; d["expectedResults"][0]["unit"]=None
    result,_=evaluate([d],{d["documentId"]:a})
    assert result["overall"]["UnsafeAcceptedUnitRangeErrors"]==1
    assert any(e["severity"]=="C" for e in result["errors"])


def test_swapped_values_are_not_used_to_rematch_names(sample):
    d,a=sample; a["results"][0]["valueNumeric"],a["results"][1]["valueNumeric"]=a["results"][1]["valueNumeric"],a["results"][0]["valueNumeric"]
    result,_=evaluate([d],{d["documentId"]:a})
    assert result["overall"]["UnsafeAcceptedAssociationErrors"]==2
    assert result["overall"]["wrongAssociations"]==2
    assert result["overall"]["wrongValueAssociations"]==2


def test_range_and_unit_swaps_are_explicit(sample):
    d,a=sample
    for f in ["unit","originalUnit","normalizedUnit","referenceText","referenceType","referenceMin","referenceMax","referenceOperator"]:
        a["results"][0][f]=a["results"][1][f]
    result,_=evaluate([d],{d["documentId"]:a})
    assert result["overall"]["wrongRangeAssociations"]==1
    assert result["overall"]["wrongUnitAssociations"]==1


def test_page_section_method_discrepancy(sample):
    d,a=sample; a["results"][0].update(pageNumber=99,sectionName="Wrong",methodText="Wrong")
    result,_=evaluate([d],{d["documentId"]:a})
    assert result["overall"]["rowAssociationAccuracy"]==66.6667
    assert all(e["severity"]=="B" for e in result["errors"])


def test_group_denominators_and_empty_groups(sample):
    d,a=sample; result,_=evaluate([d],{d["documentId"]:a})
    assert result["perFormat"]["Native"]["expectedRows"]==3
    assert result["perFormat"]["Scanned"]["numericAccuracy"] is None
    assert result["perPanel"]["Diabetes"]["expectedRows"]==2
    assert result["perPanel"]["Kidney"]["expectedRows"]==1


def test_ambiguous_correct_value_must_be_reviewed(sample):
    d,a=sample; d["expectedResults"][0].update(reviewRequired=True,reviewState="REVIEW_REQUIRED")
    result,_=evaluate([d],{d["documentId"]:a})
    assert result["overall"]["UnsafeAcceptedErrors"]==1


def test_review_document_verdict(sample):
    d,a=sample; a["results"][0].update(reviewRequired=True,reviewState="REVIEW_REQUIRED")
    _,_,s=compare_document(d,a)
    assert s["verdict"]=="REVIEW"
