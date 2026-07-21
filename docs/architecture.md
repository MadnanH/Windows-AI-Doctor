# Architecture

This overview is complemented by the verified [current architecture](architecture-current.md), enforceable [target rules and migration plan](architecture-target.md), and dated [architecture audit](architecture-audit.md).

WAID follows the dependency rule: Desktop and Infrastructure depend on Application; Application depends on Domain; Domain depends on nothing.

## Projects

- `WAID.Domain`: validated entities, findings, repairs, and settings.
- `WAID.Application`: scanner, repair, AI, persistence, and plugin ports plus orchestration.
- `WAID.Infrastructure`: SQLite, PowerShell, Serilog, built-in Windows scanners and repairs, local analysis, and plugin loading.
- `WAID.Desktop`: WinUI 3 composition root, navigation, dashboard, settings, and MVVM presentation.
- `WAID.Plugin.Sample`: buildable reference plugin.
- `tests`: fast domain and use-case tests.

The application layer owns workflow policy. Infrastructure adapters can be replaced without changing the UI or domain. All long-running operations accept cancellation tokens. Repairs return explicit success, detail, and restart state rather than throwing for expected operating-system failures.

## Safe repair lifecycle

All registered repairs execute through the single-reader `RepairQueue` and `RepairExecutor`. The executor requires explicit user confirmation and administrator privileges, creates a System Restore Point when the Windows capability is available, captures declared registry/file resources, and only then invokes the repair module. Failed repairs restore captured resources when the module policy permits rollback. Every attempt, including rejection and cancellation, is persisted to repair history and written to the Serilog audit trail.

## Offline diagnostic intelligence

Milestone 4 separates intelligence into four dependency-light libraries. `WAID.Health` calculates weighted category and overall scores. `WAID.KnowledgeBase` owns the embedded, version-controlled JSON rule set. `WAID.EventAnalysis` correlates related signals across scanners. `WAID.Diagnosis` evaluates rules, calculates confidence, ranks likely root causes, maps repairs, creates plain-English explanations, and builds the final report. No cloud endpoint or network dependency is involved.

PowerShell-backed Windows scanners normalize observations into stable finding codes and structured evidence. Diagnosis operates on the complete finding set rather than one scanner at a time. For example, an unexpected shutdown, SMART warning, and NTFS error jointly produce a stronger storage-failure hypothesis than any signal alone.

## Data

SQLite uses a private application database with foreign keys and an explicit schema version. Scan sessions and findings are append-only. Settings use a single validated JSON document to permit additive settings without a migration per preference.

## Security boundaries

WAID runs without elevation by default. Repairs declare whether elevation is required. PowerShell receives parameters separately from script text. Plugins are trusted in-process extensions and must be distributed through a signed, controlled channel; they are not a sandbox boundary.
