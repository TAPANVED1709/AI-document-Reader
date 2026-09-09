from timeline import build_timeline, latest_results, search_timeline, timeline_summary

def test_latest_trusted_result_prefers_corrected_and_excludes_review():
    results = [{"test":"Hemoglobin","value":10.8,"date":"2026-09-08","reviewState":"AUTO_ACCEPTED"},{"test":"Hemoglobin","value":11.2,"date":"2026-06-10","reviewState":"HUMAN_VERIFIED"},{"test":"Hemoglobin","value":108,"date":"2026-10-01","reviewState":"REVIEW_REQUIRED"}]
    assert latest_results(results)[0]["value"] == 10.8

def test_summary_is_deterministic_and_non_diagnostic():
    text = timeline_summary([{"reviewRequiredCount": 1}, {"reviewRequiredCount": 0}])
    assert text == "2 laboratory reports are available. 1 report results require source verification."
    assert not any(word in text.lower() for word in ("improved", "worsened", "diagnosis", "treatment"))

def test_missing_dates_are_retained_without_fabrication():
    results = [{"test":"TSH","value":3.2,"date":None,"reviewState":"AUTO_ACCEPTED"}]
    assert latest_results(results)[0]["date"] is None

def test_corrected_value_is_the_latest_effective_value():
    results = [{"test":"Creatinine", "value":0.9, "correctedValue":1.1, "date":"2026-09-08", "reviewState":"HUMAN_CORRECTED"}]
    assert latest_results(results)[0]["value"] == 1.1

def test_timeline_is_descending_and_supports_year_month_section_filters():
    events = [{"date":"2025-03-01", "title":"CBC", "sections":["Hematology"]}, {"date":"2026-03-01", "title":"Liver Panel", "sections":["Chemistry"]}]
    assert build_timeline(events)[0]["date"] == "2026-03-01"
    assert build_timeline(events, year=2025, month=3, section="Hematology")[0]["title"] == "CBC"

def test_timeline_search_uses_report_metadata():
    events = [{"date":"2026-03-01", "title":"Thyroid Profile", "sections":["Thyroid"]}, {"date":"2026-03-02", "title":"CBC", "sections":["Hematology"]}]
    assert len(search_timeline(events, "thyroid")) == 1
