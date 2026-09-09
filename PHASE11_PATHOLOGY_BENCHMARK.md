# Phase 11 Synthetic Pathology Benchmark

Development benchmark only; these figures are not clinical accuracy claims.

Fixture: `apps/ai-service/tests/test_phase11_pathology.py` (advanced layouts, demographic ranges, method variants, comments, OCR tokens, and 18 expected rows across CBC, LFT, renal, lipid, glucose, thyroid, iron, urine, and serology).

| Metric | Result |
|---|---:|
| Test-name exact/normalized accuracy | 18/18 (100%) |
| Numeric-value exact accuracy | 16/16 numeric (100%) |
| Unit accuracy | 16/16 supplied units (100%) |
| Reference-range accuracy | 16/16 numeric/text ranges (100%) |
| Flag extraction accuracy | 2/2 present flags (100%) |
| Section association accuracy | 18/18 (100%) |
| Row association accuracy | 18/18 (100%) |
| Comment false-positive row count | 0 |
| Method association accuracy | 2/2 explicit row methods (100%) |
| Missed-row count | 0 |

The benchmark also verifies OCR token confidence is propagated conservatively to the result confidence and that the complete source bounding box retains page and rendered-coordinate metadata.
