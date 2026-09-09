# Controlled pathology laboratory pilot protocol

This is a technical/operational evaluation plan, not a clinical validation protocol or regulatory approval. The Phase 14 synthetic corpus is an engineering challenge set. It does not establish performance on a laboratory's patients, instruments, languages or templates.

## Scope and selection

Select a laboratory with an accountable pathology lead, a qualified laboratory reviewer, an IT/security contact and a documented correction/escalation route. Agree which report templates, analytes, methods and scanners are in scope before collecting data. Start in shadow mode: the reader's output must not replace the original laboratory report or feed an unattended clinical decision system.

Use consecutive or randomly selected eligible reports, with a written inclusion/exclusion log, rather than choosing reports that the reader handles well. Keep a representative sampling stratum and a separate challenge stratum. Include single-test, CBC, biochemistry, coagulation, endocrine, serology and urine reports; native, scanned and mixed PDFs; scanner vendors/resolutions; faint copies; wrapped rows; parallel columns; multiple pages; qualitative results; printed age/sex ranges; repeated methods and unusual values. Report sample sizes separately. Balance native/scanned inputs according to the lab's actual workload and retain deliberately difficult cases as a separately reported cohort.

## Privacy, access and retention

Before transferring any real report, the laboratory's privacy owner must approve the purpose, access list, lawful handling basis and any applicable consent requirements. This protocol does not determine those requirements for the laboratory.

Create de-identified copies within the lab's controlled environment. Remove patient names, DOB, phone/address, accession numbers, government IDs, patient numbers, barcodes/QR codes, identifying filenames and PDF metadata. Inspect every page visually and search extracted text; a visible black rectangle alone is insufficient. Use a random pilot identifier whose mapping, if needed, remains with the lab. Retain only the minimal age band/sex information required to evaluate printed demographic ranges, subject to the approved handling plan. Re-identification keys must never enter the repository.

Do not commit real reports or ground truth to Git. Store approved pilot artifacts in encrypted, access-controlled local storage. Preserve authentication, CSRF, organization boundaries, patient ownership, RBAC, PDF access controls and audit logging. Do not use public OCR/medical AI services. Agree an explicit retention deadline before intake, including backups and exports; the lab custodian documents deletion and any authorized incident-related hold. Synthetic repository fixtures may be retained as engineering regressions.

## Independent truth and review

A qualified laboratory scientist or pathologist familiar with the report format must transcribe all printed results, units, reference text (including every age/sex segment), flags, sections, page numbers and methods. Preserve exact numeric values and comparison operators. Do not substitute external reference ranges or silently normalize medically unusual results.

Truth creation must precede inspection of reader output. Freeze the original PDF, truth and their hashes. Use independent second review for all challenge cases and at least a prespecified random sample of representative cases; preferably double-review every row. Record disagreements, reviewer identity and adjudication by the pathology lead. Ambiguous or illegible source material gets an explicit ambiguity label, not an invented numeric truth. Keep clean pre-degradation values separately when deliberately damaging a synthetic scan.

Truth corrections require a dated reason, source evidence, independent approval and a new version. Recompute both baseline and fixed-system scores against that version while preserving previous versions. Never adjust truth just to improve a parser score.

## Execution and human workflow

1. Authenticate a lab-scoped staff account and ingest with an authorized patient association and idempotency key.
2. Record ProcessingJob, queue wait, terminal status, engine/build versions, processing mode and per-page source. Let the normal worker call the local extraction and validation service.
3. Export and hash the persisted **machine baseline before review**. Keep it immutable for scoring. Record missing reports, failed jobs and missed rows; do not drop them from denominators.
4. A reviewer compares **all** source rows with output during the pilot, including AUTO_ACCEPTED rows and report-level omissions. Reviewing flagged rows alone cannot measure false negatives.
5. Open the review queue, correct supported fields, verify the result, and retain correction/security audit history. Link the before/after snapshots and reviewer reason to the mismatch log. A corrected result never replaces the machine baseline in accuracy metrics.
6. Categorize root cause (OCR, layout, range/metadata selection, normalization, persistence, review logic or truth defect). Escalate omissions and corrections unsupported by the current UI to the pathology lead; do not improvise an untracked edit.

The current application supports row correction/verification. The pilot harness exercises an actual numeric correction where a numeric mismatch exists. This is not a claim that every possible correction field or report-level omission has a complete workflow.

## Measurements and engineering gate

Compare every expected row and field, including absent fields. Require exact Decimal numeric equality; document any whitespace or dash-format equivalence. Match rows using identity/source association, never by selecting whichever value makes a comparison pass. List every false positive and missed row separately. Publish numerator and denominator, overall and by report format/panel, with no 100% claims for empty groups.

Report original/normalized names, numeric/text values, original/normalized units, raw range/type/bounds/operator, flags, calculated status, page/section, method and review-state accuracy. Report row association and every swapped/wrong association. Report document PASS/REVIEW/FAIL as engineering summaries only.

Review confusion uses incorrect or source-ambiguous rows as positives. Report TP, FP, FN, TN, recall, precision, false review rate and SafeFailureRate. Missed rows remain errors and review false negatives; a generic report review flag is not proof that an omitted row was detected. Include auto-accepted false-positive rows in UnsafeAcceptedErrors. Separate unsafe numeric, association and unit/range errors; these subcounts may overlap.

Engineering severity: A wrong numeric value auto-accepted; B wrong test/value/page/section/method association or fabricated row; C wrong unit/reference association; D missed result; E other metadata/format/review discrepancy (including numeric errors already routed to review). These labels are not clinical severity grades.

The internal hard safety gate is **UnsafeAcceptedErrors = 0** on the frozen pilot. Also publish actual numeric and row-association accuracy, review recall/precision, false positives and omissions. The lab and engineering owners must review any remaining errors and workload before expanding scope. Do not infer a universal clinical threshold or statistical safety guarantee from a zero observed count. Validate fixes on the complete frozen corpus and, before expansion, an independently authored holdout corpus.

## Incidents and release decision

If an incorrect result is trusted, a cross-patient/organization association occurs, or unauthorized access is suspected, pause pilot expansion and any downstream use. Preserve the original and machine snapshot, audit trail, build and fixture hashes. Notify the named lab and engineering owners using the agreed internal route; assess scope and rollback where appropriate. Add a regression, apply a minimal fix, rerun the complete pilot, retain before/after evidence and obtain documented acceptance from both owners.

At closure, publish the complete mismatch register, adjudications, limitations, group sizes, regression results and unresolved issues. The lab owns the decision to conduct a real-lab shadow pilot. No autonomous clinical use follows from a successful synthetic run.
