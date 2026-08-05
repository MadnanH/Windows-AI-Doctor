# v1.0 release checklist

WAID remains scope-frozen except for defects that block a recorded release control. The authoritative machine-readable control set is `release/release-candidate.json`; `scripts/Invoke-ReleaseCandidateValidation.ps1` produces the decision and treats missing evidence as failure.

## Candidate preparation

- [x] Repository version and release manifest agree at `0.40.0-dev`.
- [x] Restore, warning-free Release build, 406 non-destructive tests, quality policy, knowledge validation, accessibility smoke, performance gate, lifecycle contract, and packaging contract pass locally.
- [x] Installed and portable x64 artifacts launch their CLI and pass hashes/dependency checks.
- [x] Installed and portable ARM64 artifacts pass cross-architecture integrity/dependency checks.
- [x] Manual evidence excludes executable paths and records the artifact filename and SHA-256.
- [ ] The exact candidate commit has a successful immutable CI `release-gate` result.
- [ ] A protected release environment supplies final identity assets and a trusted matching certificate.
- [ ] Signed MSIX hashes, signature verification, and lifecycle evidence are archived.

## Release-lab acceptance

- [ ] Windows 10 x64, Windows 11 x64, and native Windows 11 ARM64 pass.
- [ ] Standard-user, administrator, offline, unsupported-hardware, policy, and plugin flows pass.
- [ ] Keyboard, Narrator, high contrast, 200% scaling, localization, first-run, and error recovery pass.
- [ ] Scan, offline AI diagnosis, reports, monitoring, persistence, legacy migrations, and crash recovery pass.
- [ ] A disposable snapshotted administrator VM passes approval, restore point, backup, repair, restart, validation, and rollback checks.
- [ ] Supported hardware-provider variants pass without unavailable data being presented as healthy.
- [ ] Long-duration performance/soak budgets pass on the supported hardware matrix.
- [ ] Security review, vulnerability audit, secret scan, privacy export inspection, and signed-package trust pass for the exact commit.

## Decision

Run the validation harness with the repository-local or system .NET 8 SDK. A production release is allowed only when it emits `GO`. The current evidence-backed decision is **NO-GO** because required signed-package, CI, real-Windows, accessibility, destructive-VM, hardware, security-review, and long-soak evidence has not been supplied. Passing automated tests alone cannot change that decision.
