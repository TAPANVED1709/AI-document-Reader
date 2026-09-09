# Operations incident runbook

Start with `docker compose ps`, `/health`, and `/health/ready`. Preserve the
request or job correlation ID and safe error code. Do not copy report contents
into tickets or logs.

- **API down:** inspect proxy and API logs, restart API, then verify readiness.
- **AI service down/OCR failure:** extraction remains reviewable; restore the
  container and confirm Tesseract readiness before retrying affected reports.
- **SQL unavailable:** do not recreate the database; restore connectivity or a
  verified backup and inspect failed processing state.
- **Queue or processing stuck:** stop new uploads, inspect bounded retries and
  disk/CPU pressure, then restart the worker after preserving state.
- **Disk full:** stop uploads, expand or rotate approved storage, and verify
  file ownership before resuming.
- **Ollama unavailable:** leave local AI explanations degraded; deterministic
  pathology extraction must remain operational.
- **Unexpected surge:** apply the configured rate limits and concurrency caps;
  never remove authorization or validation checks.
