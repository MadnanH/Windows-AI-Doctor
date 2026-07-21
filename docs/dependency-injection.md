# Dependency injection and composition conventions

WAID has one production composition root: `WAID.Desktop.App`. Application services and ViewModels receive dependencies exclusively through constructors and must never accept `IServiceProvider` or call `GetService`/`GetRequiredService`. An architecture regression test enforces that rule.

## Host configuration

`WaidHostOptions` is the version 1 host contract. It contains only non-secret runtime locations, plugin host/security policy, and the executable path. Paths must be absolute, the configuration version must match, the host version must be valid, and at least one publisher must be allow-listed. Passwords, tokens, certificate private keys, and signing credentials are not valid host options and must remain in protected deployment infrastructure.

The desktop creates defaults once and passes the validated record to Infrastructure and plugin registration. Tests and future hosts should construct the same record explicitly. The legacy `AddWaidInfrastructure(string dataDirectory)` overload remains supported and delegates to the typed defaults.

## Feature modules

`AddWaidInfrastructure` composes deterministic feature registration methods in this order:

1. structured logging;
2. SQLite persistence;
3. replaceable Windows platform adapters;
4. scanners and report exporters;
5. safe repair modules;
6. offline diagnosis;
7. monitoring, scheduling, evidence, and prioritization.

Each group records a `WaidModuleStatus`. The Plugins screen exposes those statuses alongside plugin load diagnostics. Feature modules register services against Application interfaces where a port exists. Windows API, PowerShell, filesystem, and SQLite implementations remain in Infrastructure.

## Startup validation

After plugins and presentation services are added, `BuildValidatedWaidServiceProvider` checks:

- required contracts are present exactly once and use singleton lifetime;
- constructor graphs and scopes are valid;
- typed options remain valid;
- all required contracts resolve;
- scanner and repair IDs are non-empty in aggregate and unique, including plugin contributions.

Failures use `WaidStartupException` with a stable code, safe user message, and recovery action. The desktop displays these in a non-privileged recovery window and starts no scanner or repair. Unexpected details are reduced to exception type and a redacted message in local crash diagnostics.

## Extension rules

- Add a feature to the closest existing module; create another module only for a cohesive independent subsystem.
- Prefer one registration per singular contract. Multiple implementations are allowed only for intentional collections such as `ISystemScanner` and `IRepairModule`.
- Use singleton for stateless/thread-safe services and host-lifetime coordinators. A different lifetime requires a documented ownership reason and a validation test.
- Do not perform privileged work during registration or resolution. Administrator checks occur when a repair executes.
- Avoid network, scan, or repair work during startup. Database initialization remains the sole compatibility side effect and is addressed by the ordered migration work in Prompt 04.
- Plugin registration failures become quarantine diagnostics and do not terminate the host. Duplicate scanner/repair IDs still fail closed during validation.
