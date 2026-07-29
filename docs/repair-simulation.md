# Repair Simulation and Dry Run

WAID creates a read-only, versioned dry run before any repair can be approved. Simulation calls only the repair plan and simulation-definition contracts. It does not invoke the module executor, request elevation, create a restore point, write a backup, run PowerShell, or mutate Windows state.

## Predicted effects

Each effect has a stable order, kind, sanitized target, before state, predicted after state, certainty, and rationale. Supported kinds are file, registry, service, scheduled task, policy, command, restart, storage space, and prerequisite. Certainty is explicit:

- `Exact` means the registered definition declares the effect directly, such as whether it requires a restart.
- `Estimated` means the target is known but the resulting Windows value depends on runtime state.
- `Unknown` means execution is required to observe the outcome, such as command output.

Production PowerShell repair modules provide typed command, resource, and restart effects. The generic simulator safely describes third-party repair modules that do not implement the optional definition-provider contract and marks unknowns rather than inventing results.

## Determinism and validity

Simulation version `repair-simulation-1.0` canonicalizes the repair identity, description, policy, ordered resources, prerequisites, conflicts, predicted effects, assumptions, warnings, duration, storage estimate, and restart declaration. SHA-256 produces the content fingerprint. Equivalent inputs produce the same fingerprint regardless of resource ordering.

A dry run is valid for 15 minutes. Immediately before approval and execution, WAID regenerates the current plan and fingerprint. A version change, expiry, definition change, policy change, resource change, dependency or conflict change, or predicted-effect change cancels the lifecycle. The user must simulate and review again. A stale preview can never be treated as current.

## Persistence and privacy

SQLite schema 27 adds indexed simulation version, fingerprint, and UTC timestamp columns to the durable orchestration record. The JSON lifecycle payload stores the complete predicted effects, assumptions, warnings, duration, storage estimate, restart prediction, validity window, and rollback policy. User-profile path prefixes are replaced with `%USERPROFILE%`; simulation does not read file contents, registry values, tokens, product keys, or personal documents.

## UI

The Repair Lifecycle page shows the version and short fingerprint, creation and expiry times, before/after effects, effect certainty, estimated duration, restart prediction, warnings, assumptions, and rollback readiness before the existing approval controls. High-risk acknowledgement and every Prompt 25 safety gate remain unchanged.

## Limitations

Dry-run output predicts declared behavior, not the exact future Windows result. Command output, service timing, available disk space, restore-point availability, and operating-system side effects may remain unknown until preparation or execution. Current built-in modules estimate a five-minute duration and do not claim a storage-space figure. Real Windows acceptance still requires disposable-VM validation; simulation is not a substitute for backups, restore points, validation, or explicit approval.