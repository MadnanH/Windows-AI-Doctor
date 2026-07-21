# Current architecture baseline

Verified against commit `c1277a6` on 2026-07-21. The baseline restored successfully, built in Release with zero warnings and zero errors, and passed 112 of 112 tests.

## Project dependency map

```text
WAID.Desktop (WinUI composition root)
├── WAID.Application
└── WAID.Infrastructure
    ├── WAID.Application
    ├── WAID.Diagnosis
    ├── WAID.EventAnalysis
    ├── WAID.Health
    ├── WAID.KnowledgeBase
    └── WAID.Domain

WAID.Application
├── WAID.Diagnosis
└── WAID.Domain

WAID.Diagnosis
├── WAID.EventAnalysis
├── WAID.Health
├── WAID.KnowledgeBase
└── WAID.Domain

WAID.EventAnalysis ──> WAID.Domain
WAID.Health ─────────> WAID.Domain
WAID.KnowledgeBase ──> WAID.Domain
WAID.Plugin.Sample ──> WAID.Application
WAID.Domain ─────────> no WAID project
```

There are no circular project references. The compiled dependency graph is checked by an automated architecture test. `WAID.Application` directly references Diagnosis and Domain; its public models also expose types that cause compiled references to the transitive EventAnalysis and Health assemblies, so its diagnosis use case is not currently a pure port boundary. Changing that is optional and must be incremental.

## Project inventory

| Project | Ownership | Classification | Evidence |
|---|---|---|---|
| `WAID.Domain` | Findings, scan lifecycle, settings, repair policies/results/transactions | Complete | Domain tests and dependency rule |
| `WAID.Application` | Scanner, persistence, AI, repair, report and plugin contracts; orchestration | Complete with boundary debt | Application tests; no Infrastructure reference |
| `WAID.EventAnalysis` | Cross-scanner correlation | Complete | Diagnosis tests |
| `WAID.Health` | Weighted category and overall scoring | Complete | Diagnosis tests |
| `WAID.KnowledgeBase` | Embedded versioned offline rules and references | Complete | Schema/migration/validation tests and CI check |
| `WAID.Diagnosis` | Rules, confidence, root causes, explanation, recommendations and reports | Complete | 46 diagnosis tests |
| `WAID.Infrastructure` | Windows, PowerShell, SQLite, Serilog, plugins and exports | Complete with platform validation pending | 36 tests; x64 publish and ARM64 compile |
| `WAID.Desktop` | WinUI shell, MVVM screens and composition | Partial verification | Build and XAML/navigation smoke pass; real assistive-technology and platform runs pending |
| `WAID.Plugin.Sample` | Reference PATH scanner and manifest | Complete as SDK example | Real scanner test and solution build |

## Runtime composition

`App` is the single composition root. It creates versioned `WaidHostOptions`, calls the modular Infrastructure and plugin registrations, adds view models and the main window, then calls `BuildValidatedWaidServiceProvider`. Validation checks required registrations, singleton lifetimes, constructor graphs, scopes, options, and unique scanner/repair IDs before the schedule loop or UI can start. A typed failure opens a safe recovery window and starts no diagnostic or repair operation. Service registration is covered by provider, configuration, duplicate, lifetime, and architecture regression tests.

Configuration is resolved behind Application-owned contracts. Infrastructure combines validated machine and administrator-policy files with SQLite user/profile state and in-memory session overrides. The resolver produces a frozen operation snapshot; policy is always last, and experimental flags fail closed behind an explicit master gate.

The two `GetRequiredService` calls in `App.OnLaunched` are composition-root resolution, not domain-level service locator usage. Factory registrations inside `DependencyInjection` resolve constructor dependencies and remain inside the composition boundary. No other production layer receives `IServiceProvider`.

## Coupling and mutable-state findings

- `WAID.Desktop.App` chooses `%LOCALAPPDATA%`, obtains the process path indirectly through infrastructure registration, and writes a last-resort crash file. This is acceptable bootstrap coupling but should move behind application-owned environment/crash ports if alternate hosts are introduced.
- `OperationsViewModel` resolves the Windows minidump directory. Move path discovery behind the crash-analysis port before adding a CLI or service host.
- Serilog is created lazily by a host-owned logging-provider registration and disposed with the validated service provider; WAID no longer configures process-global `Log.Logger` state.
- Plugin loader contexts and catalog contents are mutable by design and host-owned. Loaded contexts persist until restart because the DI container may retain plugin types.
- Static dictionaries, regexes, JSON options, and health weights are immutable lookup/configuration objects; they are not mutable application state.

## Failure, security, and privacy boundaries

- Repairs require explicit confirmation and administrator checks at execution time, then apply restore-point/backup/rollback policy and audit persistence.
- Scanner calls use cancellation, bounded timeouts, one bounded read-only retry, and typed terminal states encoded in degraded findings.
- PowerShell parameters are separate from scripts. Expected provider absence becomes an unavailable finding; unexpected failures are logged and isolated.
- Exports exclude credential-like names and redact secret-like values and the user-profile path. Raw SQLite/settings are excluded from diagnostic packages.
- Plugin manifests validate API, host version, path containment, publisher policy, optional Authenticode trust, and failure quarantine. In-process plugins are an extension boundary, not a security sandbox.

Known gaps: plugin state-file parse failure currently falls back to an empty disabled set; the crash-log fallback intentionally cannot log its own write failure; several UI view models expose failures as status text rather than a shared typed presentation model.

## Delivery inventory

- Tests: four xUnit projects, 112 tests at baseline.
- CI: restore, Release build/test, knowledge validation, accessibility smoke, package vulnerability audit, secret scan, x64 publish, and ARM64 compile.
- Packaging: x64/ARM64 publish support and an MSIX manifest template; signed installer validation remains unverified.
- Documentation: architecture, development, plugins, milestone verification/operations, manual validation, security policy, progress ledger, and packaging guidance.
