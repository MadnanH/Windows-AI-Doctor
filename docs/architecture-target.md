# Target architecture and rules

The target preserves the current projects and moves boundaries only when a verified defect or new host requires it. This is an incremental plan, not a rewrite.

## Dependency rules

1. `WAID.Domain` references no WAID project and no Windows/UI/persistence package.
2. `WAID.EventAnalysis`, `WAID.Health`, and `WAID.KnowledgeBase` may reference only Domain.
3. `WAID.Diagnosis` may reference Domain and the three offline intelligence libraries; it may not reference Application, Infrastructure, or Desktop.
4. `WAID.Application` may reference Domain and offline diagnosis contracts/implementation while the desktop remains the only host. It may never reference Infrastructure or Desktop.
5. `WAID.Infrastructure` implements Application ports and may reference offline libraries; it may not reference Desktop.
6. `WAID.Desktop` is the composition root and presentation layer. OS calls belong behind application-owned ports except unavoidable WinUI bootstrap behavior.
7. Plugins reference the public plugin/application contracts, never Infrastructure or Desktop.
8. Long-running contracts accept `CancellationToken`; operating-system calls have bounded execution; retries are explicit and bounded.
9. Expected operational failures use typed results/statuses. Exceptions represent programming, contract, or unrecoverable initialization failures.
10. Repairs always require explicit user approval. Elevation is requested or checked only when a privileged action executes.

These project-reference rules are enforced in `ArchitectureDependencyTests`. Registration completeness remains enforced in `DependencyInjectionTests`.

## Target flow

```text
WinUI / future CLI
        │ commands and view state
        ▼
Application use cases and ports
   ├── offline diagnosis libraries
   ├── scanner/repair contracts
   └── persistence/export contracts
        ▲
        │ adapters
Infrastructure: SQLite, Windows APIs, PowerShell, Serilog, plugins, files
```

Domain and offline diagnosis remain deterministic and network-free. Infrastructure validates external Windows, file, plugin, and database input before normalization. Presentation consumes normalized models and actionable failure states.

## Incremental migration sequence

1. **Composition hardening — completed by Prompt 02:** registrations are grouped by logging, persistence, Windows adapters, diagnostics/reporting, repairs, offline diagnosis, continuous operations, and plugins; versioned options and container validation are enforced; logging is host-owned.
2. **Migration reliability:** replace the single create-all SQL batch with ordered, transactional migrations from every supported `user_version`; reject newer schemas; add upgrade and interrupted-migration tests.
3. **Host-neutral environment ports:** move minidump path, app-data path, startup launch, and crash sink selection behind Application contracts before a CLI/service host is added.
4. **Typed operational failures:** adopt a shared failure code/detail model for repository, plugin-state, report, and UI operations. Preserve exception logging and user-actionable text.
5. **Contract packaging:** if third-party plugins become a shipped feature, extract stable plugin contracts into a small versioned SDK package without moving existing implementations.
6. **Release validation:** execute the JSON-backed OS, architecture, privilege, offline, accessibility, and destructive-VM matrices before declaring certification.

Each step must independently restore, build with zero warnings, pass all tests, preserve SQLite data, and update the progress ledger. No step combines schema migration with UI redesign.
