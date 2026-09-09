# Production deployment runbook

## Prerequisites

Install Docker Compose v2, provision a private Docker network, persistent
volumes, a DNS name, and a TLS certificate. Copy `.env.example` to a protected
environment file and replace every placeholder secret. Never commit that file.

## Startup

Run `docker compose config`, then `docker compose build`, then `docker compose
up -d`. The SQL Server and report storage volumes are persistent. Production
startup uses additive schema initialization and must never use `EnsureDeleted`,
drop tables, or recreate an existing database.

## Configuration

Set `ConnectionStrings__DefaultConnection`, `CORS_ORIGIN`, upload and rate
limits, `Storage__RootPath`, and the local LLM variables. Tesseract and English
language data are installed in the AI image. Ollama is optional; deterministic
extraction and validation remain available when it is down.

## TLS and proxy

Put the certificate and private key in a deployment secret mount at
`infra/nginx/certs`, restrict permissions, and expose only port 443. Do not
publish SQL Server, AI service, or Ollama ports. Rotate certificates before
expiry and verify HSTS after deployment.

## Operations

Check `/health` for dependency detail and `/health/ready` for readiness. Inspect
container logs for request IDs and safe error codes. Logs must not contain PDFs,
OCR text, credentials, cookies, or tokens.

## Backup, upgrade, rollback, shutdown

Back up SQL Server daily with `BACKUP DATABASE` and snapshot the report volume
daily. Test restores in an isolated environment using
`docs/BACKUP_RESTORE_RUNBOOK.md`. Upgrade images only after tests and `docker
compose config`; rollback by restoring the previous image tags and database
backup. Use `docker compose down` without `-v` for normal shutdown.
