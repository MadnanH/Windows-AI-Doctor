# Backup, Restore Point, and Rollback System

WAID uses layered recovery and never describes a repair as reversible merely because it requested a backup. Reversibility is based on a validated capability and a verified outcome.

## Recovery tiers

- `None`: no usable recovery evidence exists. Execution is blocked when the repair policy requires backup.
- `RestorePointOnly`: Windows accepted a restore-point request, but no repair-specific artifact is available. Restore-point availability is not treated as guaranteed rollback.
- `ResourceBackup`: every declared file, directory, or registry resource was captured; storage, access protection, completeness, manifest, and SHA-256 item validation passed.
- `VerifiedRollback`: a valid artifact was restored and every supported restore action passed provider or post-restore verification.

The central repair executor accepts only `ResourceBackup` or better for a backup-required repair. It records `RolledBack` only when the rollback result is both successful and verified.

## Artifact creation and protection

Before copying resources, WAID estimates required bytes and requires the target volume to have that capacity plus a safety reserve. The transaction directory is confined below the configured WAID backup root. Windows ACL inheritance is removed and access is restricted to the current account, Local System, and local Administrators. Failure to verify that protection makes the artifact unusable.

Files and directories are copied without reading their application-level contents. Registry keys are exported through `reg.exe`. Each item receives a SHA-256 hash and byte length. A versioned manifest records the transaction, UTC creation and expiry, and item metadata; the manifest itself is hashed. Snapshot metadata must match the manifest and durable database record before rollback.

Creation is idempotent per transaction. A repeated request returns the existing artifact only when all hashes still validate. Incomplete, inaccessible, low-space, malformed, expired, or altered artifacts fail closed with typed validation state.

## Restore points

The Windows restore-point provider is capability-checked before creation. Successful results record the provider identifier, description, and UTC creation time. An unavailable provider remains explicit; it is never represented as a successful restore point. Repair-specific backup policy continues independently.

## Rollback and verification

Rollback validates the manifest and every artifact hash before changing Windows. Files and directories are restored in reverse order and then hashed again. Registry import must be confirmed by the Windows provider. Any failed check prevents a verified outcome. No retry loop or automatic standalone rollback exists.

The Repair Lifecycle page lists artifact state, protection, capability, expiry, and validation detail. Standalone rollback requires selecting one valid artifact and checking an explicit confirmation box. The workflow revalidates the durable record and local metadata, executes through the registered rollback manager, records the outcome, and appends an audit event.

## Persistence and retention

SQLite schema 28 adds `recovery_artifacts`, indexed by transaction and expiry. Records include sanitized location, manifest hash, protection, capability, state, validation, expiry, and rollback outcome. The retention service processes at most 100 expired artifacts per run and deletes only resolved paths beneath the configured backup root. Failures are isolated and visible.

## Limitations

System Restore availability, throttling, quota, and restore success depend on Windows configuration. Registry rollback verification currently relies on successful `reg.exe import`; unlike files and directories, it does not compare every resulting registry value. ACL enforcement, restore-point behavior, disk-space races, locked files, junctions, very large trees, interruption, and rollback still require disposable-VM testing. Local administrators can alter artifacts and audit files; integrity checks detect artifact changes but do not defend against an administrator replacing both application state and records.