# Pathology Real-World Validation Protocol

This document defines a future validation process for de-identified pathology reports. The repository contains synthetic fixtures only.

1. **Data de-identification:** Remove names, dates of birth, patient and accession identifiers, contact details, addresses, barcodes, and free-text identifiers before any dataset enters validation storage.
2. **Reviewer qualifications:** Use qualified laboratory medicine or pathology reviewers familiar with the source report formats and extraction fields.
3. **Ground truth:** Reviewers record the printed test name, normalized name, value, unit, printed reference text, bounds, flag, section, page, method, and review expectation.
4. **Double review:** Have two reviewers independently label a meaningful sample where practical.
5. **Disagreement resolution:** Record disagreements, adjudicate with a third qualified reviewer, and retain the original labels and adjudication rationale.
6. **Dataset versioning:** Version the immutable document manifest, annotations, parser version, benchmark code, and metric definitions together.
7. **Lab/vendor diversity:** Include multiple laboratories, LIS vendors, analyzers, report templates, units, languages, and reference-range conventions.
8. **Scan-quality diversity:** Include native PDFs, scanner generated scans, mobile-camera scans, different printers, skew, rotation, blur, low contrast, compression artifacts, multi-column layouts, and mixed native/scanned pages.
9. **Demographic range diversity:** Include adult and pediatric reports, with male and female range layouts. Record metadata confidence and report both the complete printed range text and any selected range.
10. **Metric calculation:** Report exact numeric, textual, unit, range, flag, section, row, page, and method metrics separately. Never use synthetic or real-world extraction results as clinical outcome claims.
11. **Error review:** Manually review every wrong numeric value, wrong row association, unsafe acceptance, missed row, and high-confidence disagreement.
12. **Release thresholds:** Define internal engineering thresholds in advance, including `UnsafeAcceptedErrors = 0`. These are software release criteria, not universal medical or clinical standards.

No real patient reports or identifying data may be committed to Git.
