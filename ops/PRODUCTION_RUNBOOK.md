# Lumina Money production runbook

## Release gate

1. Put every value from `.env.production.example` in the deployment secret manager; never store populated secrets in source control.
2. Terminate TLS at a managed reverse proxy, forward `X-Forwarded-For` and `X-Forwarded-Proto`, and expose only the proxy publicly. The compose file binds the API to loopback.
3. Start one API instance with `Database__ApplyMigrationsOnStartup=true`. After migration succeeds, disable that flag and scale normal API replicas.
4. Confirm `/health/live`, `/health/ready`, `/version`, `/legal/privacy`, `/legal/terms`, `/legal/support`, and `/legal/account-deletion` through the public HTTPS hostname.
5. From a dedicated synthetic account, verify SMTP delivery, email confirmation, password reset, one-time code consumption and old-session revocation. Monitor bounces/complaints and never log codes or message bodies.
6. Configure log shipping and alerts for readiness failure, HTTP 5xx rate, authentication 429 rate, transactional-email failure, SQL saturation, storage pressure and backup failure. Logs are structured JSON in Production and include `X-Request-Id` correlation without request bodies.

## Backup policy

- Run `ops/Backup-LuminaMoney.ps1` daily against SQL Server 2022. It creates a compressed, checksum-protected `COPY_ONLY` backup and immediately runs `RESTORE VERIFYONLY`.
- Copy verified backups to encrypted, immutable object storage in a separate account/region. Use a 35-day operational retention and a documented longer legal retention only if required.
- Perform a real restore into an isolated non-production SQL Server every month. A successful backup command is not a restore test.
- Alert on a missing daily backup, verification failure, copy failure, or repository older than 26 hours.

## Incident response

1. Triage severity, stop further exposure, preserve relevant logs and rotate affected credentials.
2. Revoke refresh-token families or disable the API if account data may be exposed.
3. Establish the affected users/data/time window from correlated logs; do not put financial records in tickets or chat.
4. Notify the operator’s privacy/legal owner. Assess regulatory and customer notification deadlines, including UK GDPR where applicable.
5. Restore service from a verified artifact/backup, monitor closely, and publish a blameless post-incident review with assigned corrective actions.

## Recovery objective to validate

Target RPO: 24 hours with daily full backups; target RTO: 4 hours. These are targets until a timed restore exercise demonstrates them.
