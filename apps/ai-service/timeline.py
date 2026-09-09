from collections import defaultdict

def build_timeline(events, year=None, month=None, section=None, test_name=None, review_state=None):
    def keep(event):
        date = event.get("date") or ""
        if year is not None and not date.startswith(str(year)):
            return False
        if month is not None and date[5:7] != f"{month:02d}":
            return False
        if section and section not in event.get("sections", []):
            return False
        if test_name and test_name.lower() not in str(event.get("title", "")).lower():
            return False
        if review_state and event.get("reviewState") != review_state:
            return False
        return True
    return sorted((event for event in events if keep(event)), key=lambda event: event.get("date") or "", reverse=True)

def search_timeline(events, query):
    query = query.strip().lower()
    if not query:
        return list(events)
    return [event for event in events if query in " ".join([str(event.get("title", "")), str(event.get("date", "")), " ".join(event.get("sections", []))]).lower()]

def timeline_summary(events):
    count = len(events)
    review = sum(e.get("reviewRequiredCount", 0) for e in events)
    return f"{count} laboratory reports are available. {review} report results require source verification."

def latest_results(results):
    grouped = defaultdict(list)
    for result in results:
        value = result.get("correctedValue") if result.get("correctedValue") is not None else result.get("value")
        if result.get("reviewState") != "REVIEW_REQUIRED" and value is not None:
            grouped[result.get("test", "")].append({**result, "value": value})
    return [sorted(items, key=lambda x: x.get("date") or "", reverse=True)[0] for items in grouped.values()]
