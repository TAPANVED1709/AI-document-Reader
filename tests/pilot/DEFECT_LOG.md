# Phase 14 defect and evidence register

Ground-truth values were not changed to fit parser output. `pilot-31` was added as an explicit faded-decimal/Absent challenge after the first 30-document run. The authoritative before/after comparison uses all **31 identical PDFs and 446 identical truth rows** in `runs/baseline` and `runs/final`. The earlier 30-document evidence remains in `runs/before` and `runs/before-complete`.

| Defect | Demonstrating fixture/evidence | Minimal change | Regression |
|---|---|---|---|
| Page counters become fake results | Page rows throughout baseline | Ignore explicit page-counter syntax | `test_page_counter_never_becomes_result` |
| Native table range absorbs the next test name | pilot-03, pilot-22 | Join cells on the same baseline inside a native PDF text block | `test_native_three_column_cells_keep_own_ranges` |
| Wrapped rows duplicated; wrapped reference incomplete | pilot-09/10 | Consume recognized wrapped names once; join one numeric continuation after a dangling interval | `test_wrapped_rows_consumed_once_and_reference_completed` |
| Parallel OCR panels interleaved into overlong ranges | pilot-12; baseline SQL failure and stuck job | Infer a gutter only from repeated heading/result evidence; reconstruct baselines within each panel | `test_parallel_ocr_panels_keep_rows_and_sections` |
| Contradictory sex metadata selects first value | pilot-16 | Select metadata only when all explicit observations agree | `test_conflicting_metadata_does_not_select_first_sex` |
| Month-based printed bounds treated as years | pilot-18 | Convert printed month bounds to years before matching | `test_month_bounds_use_month_units` |
| Unrecognized Metabolic/Biochemistry headings prepend test names | pilot-08/30 | Add those explicit section headings | `test_biochemistry_and_metabolic_sections_do_not_prepend_names` |
| OCR `lron Studies` leaves following rows in Metabolic | pilot-30 | Explicit OCR heading repair; propagate low heading confidence to association review | `test_corrupted_section_heading_does_not_propagate_previous_panel` |
| Numeric rows with unparsed ranges can be trusted | pilot-03/09 | Mark numeric TEXT_ONLY range association uncertain; keep structural confidence when combining OCR confidence | `test_unknown_numeric_range_is_reviewed_not_trusted_as_text` |
| Trailing letter `t` stripped from qualitative reference text | pilot-20/21/30/31 | Correct a literal-backslash character-strip set to use a real tab | Full pilot qualitative and raw reference comparisons |
| Failed SQL insert retried in error handler, leaving PROCESSING forever | baseline pilot-12; `before/persistence-failure.txt` | Detach invalid added results/issues before saving terminal failure | `ResultInsertFailurePersistsTerminalFailureWithoutRetryingInvalidRows` |
| Correction endpoint HTTP 500 from invalid record validation metadata | baseline correction, `baseline/api-tail.txt` | Place MaxLength attributes on constructor parameters | `CorrectionBindsValidatesAuditsAndPreservesMachineValueOverHttp` |
| SQLite correction-audit endpoint cannot sort DateTimeOffset in SQL | `baseline/audit-http-regression.txt` | Sort the per-result materialized audit list in memory | Same real HTTP correction/audit regression |

## Verification harness corrections

- Windows text newline conversion initially changed truth hashes. The generator writes UTF-8 bytes and `.gitattributes` fixes truth JSON to LF. No truth values changed.
- ASP.NET intentionally omits null properties. The evaluator treats omitted/null as equivalent absence, while still rejecting absent fields when non-null values are expected. The first derived comparison overstated errors by requiring explicit null keys; the raw machine evidence is unchanged and the regression covers both omission and unexpected non-null values.
- The evaluator explicitly permits whitespace around printed range separators/operators; no numeric tolerance, case-folding or OCR-character repair is used for scoring. Raw text stays in the CSV/JSON.
- The first harness stopped at a 240-second stuck job. Subsequent baseline runs retain the stuck status as an error and continue all documents with a documented 45-second observation limit. Final runs use 240 seconds. No timed-out job is counted as completed.
- A full .NET run exposed a pre-existing test-host startup race: its shared in-memory SQLite connection opened only after the background worker started, losing the startup schema. `baseline/http-fixture-regression.txt` reproduces `no such table: ProcessingJobs`. Opening that test connection before host startup fixes the race without disabling a production worker or security check.
- The OCR confidence regression initially rejected an unnecessary `association=1` key on rows with no section. The parser now adds section association confidence only when a section exists; existing no-heading output retains its field contract.
- Under host memory pressure, the `phase14-c1e79095` baseline SQL instance's cache budget was set to 768 MB. This only affected the isolated synthetic baseline. Timings are observations, not a comparative performance benchmark.

All machine snapshots precede human correction. Baseline failures, failing regression outputs and intermediate complete runs remain available. No real laboratory report, patient identifier or external medical/OCR service was used.
