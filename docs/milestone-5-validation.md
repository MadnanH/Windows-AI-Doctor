# Milestone 5 validation record

Date: 2026-07-21

Version: 0.5.0-dev

## Automated verification

- The complete solution restores and builds in Release configuration with zero warnings and zero errors.
- All 90 tests pass with no skipped tests.
- Dependency-injection validation resolves all 17 production scanners and the diagnosis, persistence, and export workflow services.
- The integration workflow persists a scan, diagnoses correlated evidence, produces a mapped repair recommendation, rejects an unconfirmed repair, executes it only after confirmation and administrator validation, and reloads repair history.
- SQLite round trips scan sessions, findings, settings, diagnosis reports, and repair safety metadata.
- Unavailable Windows hardware/API providers return an informational unavailable finding rather than a simulated result.
- Diagnostics export tests verify the required ZIP entries and verify that the raw database and settings are excluded.

## Local launch smoke test

The self-contained x64 Release WinUI publish was launched on Microsoft Windows build 26200. It remained alive after five seconds without an early process failure and was then closed by the validation command. The framework-dependent build output correctly requires a machine-wide .NET 8 runtime and is not the delivery artifact. This confirms startup and composition-root construction for the self-contained package on the available Windows 11 host; it is not a substitute for interactive UI, scanner, or repair validation.

## Real-machine status

| Scenario | Status | Evidence required |
|---|---|---|
| Windows 11 launch | Smoke test passed | Full `Test-Windows11.ps1` JSON report still required |
| Windows 10 launch | Not tested on this host | Passing `Test-Windows10.ps1` JSON report |
| Administrator | Automated gates pass; real repair pending | Passing `Test-Administrator.ps1` report from a disposable elevated VM |
| Standard user | Automated rejection passes; interactive run pending | Passing `Test-StandardUser.ps1` report |
| Offline | Engine is fully local; interactive run pending | Passing `Test-Offline.ps1` report with networking isolated |

See `manual-validation.md` for execution and safety instructions. No Windows 10, hardware-specific, or destructive-repair result is claimed without a captured report from the corresponding real environment.
