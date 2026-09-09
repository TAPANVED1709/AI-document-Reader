# Production security checklist

- [ ] TLS certificate installed; HTTPS and HSTS verified.
- [ ] Secure, HttpOnly session cookie and CSRF protection verified.
- [ ] `Cors__AllowedOrigins` contains only approved HTTPS lab origins.
- [ ] Only the reverse proxy is publicly reachable on 443.
- [ ] SQL Server, AI service, Ollama, and storage are private network services.
- [ ] Secrets are injected through deployment configuration, never images or Git.
- [ ] Server-generated PDF names, magic-byte checks, size limits, and path safety verified.
- [ ] Storage and SQL volumes use restricted service identities and daily backups.
- [ ] Rate limits, page/resource limits, and bounded retry limits configured.
- [ ] Logs contain correlation IDs and safe codes, without report contents or tokens.
- [ ] Authentication, RBAC, IDOR, organization isolation, CSRF, CORS, and audit tests pass.
- [ ] Restore procedure has been executed in an isolated environment.
