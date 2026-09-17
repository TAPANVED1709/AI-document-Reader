# Phase 15C review calibration analysis

Executed against the current parser and validator with local Tesseract. Synthetic engineering benchmark; not clinical validation.

25 documents, 133 expected/extracted rows; 24 false-review rows relative to the existing benchmark expectations. Review recall 100%, precision 4%, unsafe accepted errors 0. Numeric accuracy 99.14%; all other measured field accuracies 100%; no missed or false-positive rows. The one numeric mismatch is the deliberately damaged decimal-loss case and remains under review.

No parser, validator, threshold, or ground-truth changes were made. Matching uses each benchmark row exactly once, including repeated tests. Unlike the previous issue-only summary, this includes rows with reviewRequired=true but no validationIssues entry.

## Cause counts

Categories overlap. Unique primary categories sum to 24.

| Cause | Rows with cause | Primary rows |
|---|---:|---:|
| name confidence | 3 | 1 |
| range association | 2 | 0 |
| unit confidence | 7 | 5 |
| demographic applicability | 0 | 0 |
| numeric safety | 2 | 2 |
| layout/wrapped row | 0 | 0 |
| other: qualitative UNKNOWN policy | 17 | 16 |

## All 24 reviewed rows

Ordinal is the one-based matched result position across the benchmark, disambiguating repeated tests.

| Ordinal | Fixture | Test | Causes | Confidence |
|---:|---|---|---|---:|
| 13 | qualitative-urine-serology | Protein | other: qualitative UNKNOWN policy | 0.95 |
| 14 | qualitative-urine-serology | HIV | other: qualitative UNKNOWN policy | 0.95 |
| 28 | panel-sweep | HBsAg | other: qualitative UNKNOWN policy | 0.95 |
| 101 | expanded-panel-09 | pH | unit confidence | 0.55 |
| 102 | expanded-panel-09 | Specific gravity | unit confidence | 0.55 |
| 103 | expanded-panel-09 | Protein | other: qualitative UNKNOWN policy | 0.95 |
| 104 | expanded-panel-09 | Glucose | other: qualitative UNKNOWN policy | 0.95 |
| 105 | expanded-panel-09 | Ketones | other: qualitative UNKNOWN policy | 0.95 |
| 106 | expanded-panel-09 | RBC/hpf | unit confidence | 0.55 |
| 107 | expanded-panel-09 | WBC/hpf | unit confidence | 0.55 |
| 108 | expanded-panel-09 | Bacteria | name confidence, other: qualitative UNKNOWN policy | 0.7 |
| 109 | expanded-panel-10 | HBsAg | other: qualitative UNKNOWN policy | 0.95 |
| 110 | expanded-panel-10 | HIV | other: qualitative UNKNOWN policy | 0.95 |
| 111 | expanded-panel-10 | HCV | other: qualitative UNKNOWN policy | 0.95 |
| 112 | expanded-panel-10 | VDRL | other: qualitative UNKNOWN policy | 0.95 |
| 113 | expanded-panel-10 | CRP | other: qualitative UNKNOWN policy | 0.95 |
| 114 | expanded-panel-10 | Rheumatoid factor | other: qualitative UNKNOWN policy | 0.95 |
| 115 | expanded-panel-10 | HIV | other: qualitative UNKNOWN policy | 0.95 |
| 116 | expanded-panel-10 | HCV | other: qualitative UNKNOWN policy | 0.95 |
| 120 | real-ocr-lft | AST | numeric safety, name confidence, range association, unit confidence | 0.36 |
| 123 | real-ocr-hba1c | HbA1c | numeric safety, name confidence, range association, unit confidence | 0.47 |
| 125 | real-ocr-urine | Protein | other: qualitative UNKNOWN policy | 0.95 |
| 126 | real-ocr-urine | Glucose | other: qualitative UNKNOWN policy | 0.95 |
| 127 | real-ocr-urine | RBC/hpf | unit confidence | 0.55 |

## Conservative decisions retained

- Two OCR rows have correct fixture values but low measured numeric/name/range confidence. Knowing the synthetic answer does not establish safety for unseen reports; their review flags remain.
- Five numeric urine rows omit a separate unit column; the two OCR rows also have low unit confidence (seven rows with this cause overall). Missing-unit confidence remains conservative; there is no general policy in this closure to infer units from test names or silently treat every missing unit as dimensionless.
- Seventeen qualitative rows retain text without a fabricated numeric value. Their numeric calculated status is UNKNOWN, which currently requires review. One also has low name confidence (Bacteria). Changing this would require a separately validated qualitative acceptance policy.
- No false-review rows are caused by demographic ambiguity or wrapped-row/layout association in this dataset; those safety controls remain intact.

Zero unsafe accepted errors and 100% recall are limited to this synthetic dataset. No threshold was relaxed to improve precision.
