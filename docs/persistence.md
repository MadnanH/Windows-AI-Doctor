# SQLite Persistence, Migration, and Recovery

WAID owns one local SQLite database at `%LOCALAPPDATA%\Windows AI Doctor\waid.db`. Infrastructure is the only layer that references `Microsoft.Data.Sqlite`; application and domain projects contain contracts and business models, not persistence entities.

## Schema 9

| Area | Tables | Retention and ownership |
| --- | --- | --- |
| Scanning and evidence | `scan_sessions`, `scanner_executions`, `findings`, `evidence` | Scan sessions atomically own versioned executions, findings, observations, failures, duration, and resource estimates. |
| Diagnosis and reports | `diagnosis_reports`, `reports` | Diagnosis rows belong to scan sessions; report metadata contains local output references. |
| Repairs and recovery | `repair_history`, `repair_approvals`, `rollback_records` | Repair history owns rollback metadata. Approval records remain auditable. |
| Monitoring | `health_snapshots`, `timeline_events`, `metrics`, `alerts`, `scan_schedule` | Time-indexed operational history and the singleton schedule. |
| Product state | `settings`, `configuration_state`, `policies`, `plugins`, `chats` | Versioned configuration state preserves legacy settings, sources, active profile, and feature choices. |
| Governance | `audit_events`, `schema_migrations` | Reserved durable audit catalog and applied migration record. Prompt 03 security audit JSONL remains the active append-only audit store. |

The schema uses foreign keys, indexed chronological queries, a WAID application identifier, and `PRAGMA user_version`. WAL mode, full synchronous durability, foreign keys, a five-second busy timeout, and bounded WAL checkpoints are configured for local concurrency and recovery.

## Ordered migrations

Migrations are monotonic and run in individual transactions:

1. Scans, findings, and settings.
2. Repair history.
3. Diagnosis reports.
4. Health snapshots.
5. Scan schedule.
6. Repair approvals.
7. Commercial persistence catalog, indexes, and migration history.
8. Versioned configuration state with deterministic legacy-settings migration.
9. Scanner execution lifecycle, session completion state, framework version, provenance indexes, and resource usage.

Fresh databases apply all steps. Versions 1–8 upgrade in order. Existing data is preserved through `CREATE ... IF NOT EXISTS`. Each active step commits its schema and version together; an error or interruption rolls back that step. A retry resumes from the last committed version. A database newer than the host is opened without migration and startup stops with `WAID-DB-NEWER`.

Before upgrading a non-empty versioned database, WAID uses SQLite's online backup API to create a consistent `waid-pre-migration-v*.db` copy. The five newest migration backups are retained. Migration status is available in Settings.

## Integrity, backup, and recovery

Startup runs SQLite `quick_check` before migration. The Settings page can run an additional health check covering integrity, foreign keys, schema version, and journal mode. Failures return typed reference codes and actionable recovery text.

**Create verified backup** uses SQLite's backup API while the application remains open, then runs `quick_check`. Routine backup creation retains the ten newest `waid-*.db` files. Raw file copying is not used because WAL state may otherwise be omitted.

Recovery accepts only an existing, integrity-checked database inside WAID's database backup directory with WAID's application identifier and a supported schema. It requires an explicit user acknowledgement. WAID creates a verified safety backup of the current database before replacement, restores through SQLite, reapplies any supported migrations, audits the operation, and asks the user to restart.

Do not manually edit, copy, or delete the live database, `-wal`, or `-shm` files while WAID is running. If automatic recovery fails, preserve the database and logs and use the sanitized support export before contacting support.
