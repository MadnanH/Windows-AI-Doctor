# Architecture audit and feature verification

Audit date: 2026-07-21. Baseline commit: `c1277a6`. Scope: solution projects, dependencies, public contracts, scanners, repairs, reports, plugins, persistence, UI reachability, tests, CI, packaging, security and progress claims.

## Feature classification

| Area | Status | Implementation and reachability | Automated evidence | Remaining verification |
|---|---|---|---|---|
| Scanner framework and 18 scanners | Complete | Registered in DI; Dashboard and monitoring invoke the shared coordinator | DI, scanner, orchestration and workflow tests | Provider variations on supported hardware |
| Offline diagnosis/correlation/health | Complete | Diagnosis, Health, Evidence and Repairs screens consume persisted reports | 46 diagnosis tests and workflow test | None for deterministic engine |
| Safe repair framework and six repairs | Complete | Repair screens require confirmation; executor owns admin/safeguard/rollback/audit flow | Executor, policy, rollback and workflow tests | Destructive disposable-VM evidence |
| SQLite persistence | Complete for fresh/current schema; migration path partial | All repositories use `WaidDatabase`; History/settings/diagnosis/monitoring consume them | Repository and round-trip tests | Ordered upgrades from historical schemas |
| Monitoring/schedules/crashes/evidence | Complete while app runs | Monitoring & Reports page | Cancellation, schedule, minidump, redaction and persistence tests | Real battery/hardware/provider evidence |
| Reports | Complete | HTML/JSON/ZIP/PDF controls are reachable | Export/redaction/PDF tests | Visual PDF acceptance with large reports |
| Plugins | Partial | Manifest validation, isolation/quarantine and diagnostics page exist | Plugin scanner/state/failure tests | UI enable/disable control, signed third-party corpus |
| Accessibility | Partial | Automation IDs and native keyboard/theme/scaling behavior | Navigation/XAML smoke | Screen reader, focus, high contrast and 200% manual evidence |
| CI/security | Complete as configuration | GitHub Actions, Dependabot, secret/vulnerability checks | Local build/tests; workflow syntax committed | Hosted run result depends on GitHub push |
| Packaging | Partial/unverified | x64 publish, ARM64 compile and MSIX template | Local publish/compile | Signed MSIX install/upgrade/uninstall and ARM64 launch |
| Platform certification | Unverified | Evidence-producing scripts exist | No archived passing reports | Windows 10/11/ARM64/admin/offline/hardware matrix |

No production `NotImplementedException`, fake scanner data, registry cleaner, RAM booster, silent repair, driver downloader, or antivirus replacement was found. Health scores are deterministic, weighted, and evidence-backed. Existing milestone documents may contain the word “placeholder” only as historical audit terminology.

## Public contract inventory

- Diagnostics: `ISystemScanner`, `ScanContext`, repository contracts, `IAiAnalyzer`, diagnosis models, scanner status/policy/progress.
- Repairs: `IRepairModule`, administrator/restore/backup/rollback/history ports, repair resources, policies, transaction and results.
- Continuous operation: health snapshot, schedule, approval, condition, startup, report and PDF ports.
- Extensibility: `IWaidPlugin`, `PluginMetadata`; manifest/security/catalog implementation lives in Infrastructure.
- Infrastructure-local boundary: `IPowerShellRunner` is currently declared beside its adapter. Move it to Application only if another adapter or direct use case requires it.

## Persistence baseline

SQLite is owned exclusively by Infrastructure and stored under the app's local data directory. Foreign keys are enabled in the connection string. Current `PRAGMA user_version` is 6.

| Object | Owner/data | Retention |
|---|---|---|
| `scan_sessions`, `findings` | Scan history and normalized evidence; cascade by session | No automatic deletion |
| `settings` | One validated JSON settings record | Replaced on save |
| `repair_history` | Transaction state, safeguards and event audit | No automatic deletion |
| `diagnosis_reports` | JSON report linked to scan; generated-time index | No automatic deletion; latest query available |
| `health_snapshots` | JSON monitoring snapshots; captured-time index | No automatic deletion; bounded reads |
| `scan_schedule` | One JSON schedule | Replaced on save |
| `repair_approvals` | Approval audit JSON | No automatic deletion; bounded reads |

Initialization uses `CREATE TABLE/INDEX IF NOT EXISTS` and is idempotent for a fresh/current database. Risk: it sets version 6 without ordered transitions, preflight checks, or explicit handling for a database newer than the host. Prompt 04 should introduce forward-safe transactional migrations and tests; this audit intentionally makes no schema change.

## Risks and priorities

| Priority | Risk | Consequence | Planned treatment |
|---|---|---|---|
| High | No ordered SQLite upgrade chain | Older installations may miss future column/data transforms | Prompt 04 migration runner and upgrade fixtures |
| High | Platform claims lack archived runtime evidence | Release claims could exceed verified behavior | Prompt 40 and existing JSON validation matrix |
| Medium | Monolithic registration and global `Log.Logger` | Harder host isolation and configuration validation | Prompt 02/03 incremental composition/logging changes |
| Medium | Desktop discovers minidump path | Presentation is coupled to a Windows location | Environment/crash-path port before alternate host |
| Medium | Plugin-state read failure silently enables default state | Disabled policy may be lost after corrupt state | Typed diagnostic and fail-safe policy in plugin prompt |
| Low | UI failure representation uses free-form status text | Inconsistent remediation/action mapping | Shared typed presentation failures incrementally |

## Manual validation

No GUI, hardware, administrator repair, or installation validation was performed by this architecture-only prompt. x64 Release compilation and automated XAML/navigation validation were already available; they are not substituted for archived platform reports.
