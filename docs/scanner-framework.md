# Scanner Framework Lifecycle and Evidence

WAID scanners implement the Application-owned `ISystemScanner` contract. Existing version-1 scanners remain binary/source compatible through default detailed-output behavior; new scanners should provide complete metadata and may override `ScanDetailedAsync` to return observations separately from findings.

## Metadata and planning

Every scanner exposes a stable ID, display name, description, category, semantic version, prerequisites, dependency IDs, and an optional recommended timeout. IDs are validated, unique, and must match the registration. Known prerequisites are `windows`, `powershell`, and `administrator`; unknown prerequisites fail composition or planning rather than being ignored.

The orchestrator validates the full dependency graph before a scan. Cycles stop the scan before execution. A missing, failed, timed-out, cancelled, permission-denied, or skipped dependency causes the dependent scanner to be skipped with an explicit reason. Prerequisites are evaluated immediately before execution. No elevation is requested merely to plan a scanner.

Independent scanners run in deterministic category/name plan order using bounded batches. The default maximum is three concurrent scanners and the supported range is one through eight. Scanner-specific policy overrides take precedence over metadata timeouts. A read-only `IOException` can be retried at most once; other failures are not retried.

## Lifecycle

An execution moves through these observable states:

`Planned → Running → Success | Unavailable | TimedOut | PermissionDenied | Failed | Cancelled | Skipped`

Progress includes the scanner ID/name, overall completed count, scanner-local percentage, state, and human-readable detail. Cancellation is cooperative. Completed results and execution records are retained, scanners not started are marked cancelled, the partial session is saved, and the caller still receives cancellation semantics. One scanner failure never prevents an independent scanner from running.

The Dashboard shows the plan and each scanner's current state, local progress, skipped reason, cancellation, and honest partial-completion message. Operational failure findings remain as a compatibility projection for existing diagnosis/report consumers; `scanner_executions` is the authoritative failure record.

## Evidence format

Detailed scanners return:

- `ScannerObservation`: key, string value, UTC observation time, source reference, and optional attributes.
- `DiagnosticFinding`: a conclusion with code, title, explanation, severity, optional repair mapping, and supporting evidence.
- `ScannerOutput`: the two collections above.

Legacy scanner evidence dictionaries are normalized into observations with source references in the form `scanner-id:finding-code`. Output is rejected if provenance does not match scanner metadata, required fields are blank, values exceed bounds, or collection safety limits are exceeded. Before persistence, sensitive names such as password, token, product key, serial number, device ID, and authorization are redacted; recognized user-profile paths are replaced with `%USERPROFILE%`.

Schema 9 saves the session and every finding, execution, and observation in one SQLite transaction. Execution provenance includes scanner/framework version, category, status, UTC start/end, duration, attempts, typed failure code, detail, and approximate managed-memory/CPU deltas. Process CPU and managed-memory values are diagnostic estimates because parallel scanners share the process; they are not per-thread accounting or performance claims.

## Extension guidance

Scanner implementations must be read-only, honor cancellation, avoid unbounded enumeration, return unavailable rather than infer healthy when a Windows provider does not exist, and never perform repairs. Plugins receive the same metadata validation, dependency rules, timeout, output limits, redaction, persistence, and failure isolation as built-in scanners.
