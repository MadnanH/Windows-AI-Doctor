# v1.0 validation matrix

| Control | Environment | Evidence | Current state |
|---|---|---|---|
| Full build and non-destructive tests | Windows development/CI | Harness automatic result | Pass |
| x64 installed and portable artifacts | Windows x64 | Hash/dependency and CLI launch smoke | Pass |
| ARM64 installed and portable artifacts | Windows 11 ARM64 | Hash/dependency plus native launch | Integrity pass; native launch pending |
| Windows 10 x64 | Disposable VM or supported hardware | Schema-2 manual JSON | Missing — blocker |
| Windows 11 x64 | Disposable VM or supported hardware | Schema-2 manual JSON | Missing — blocker |
| Windows 11 ARM64 | Native ARM64 VM/hardware | Schema-2 manual JSON | Missing — blocker |
| Standard user and administrator | Separate least/elevated contexts | Schema-2 manual JSON | Missing — blocker |
| Offline and unsupported hardware | Network-isolated/provider-limited VM | Schema-2 manual JSON | Missing — blocker |
| Policy, plugin, AI, scan, monitoring, report | Windows 10/11 matrix | Manual evidence and CI tests | Automated pass; manual pending |
| SQLite legacy migrations and privacy exports | Isolated integration tests plus release inspection | Test results/review evidence | Automated pass; release review pending |
| Accessibility/localization/first-run | Windows UI acceptance | Accessibility evidence JSON | Static smoke pass; manual pending |
| Signed install/upgrade/repair/uninstall | Protected release VM | Signed-package evidence JSON | Missing — blocker |
| Repair/restart/crash recovery/rollback | Disposable snapshotted administrator VM | Destructive evidence JSON | Missing — blocker |
| Hardware providers | Supported physical hardware | Hardware evidence JSON | Missing — blocker |
| Security/privacy | CI plus human review | Exact-commit security evidence JSON | Local gates pass; final review pending |
| Performance and soak | Supported release hardware | Long-soak evidence JSON | Synthetic pass; hardware soak pending |
| Exact-commit CI | GitHub Actions | Immutable CI evidence JSON | Missing — blocker |

Evidence JSON belongs under `artifacts/release-evidence`, is not committed, and follows `release/evidence.schema.json`. Every record binds to schema 2, the exact candidate ID, product version, 40-character commit, scenario, recent UTC timestamp, and result. The harness rejects stale, future, wrong-candidate, wrong-version, wrong-commit, password/token/product-key/profile/path fields, and any expanded local user-profile path.
