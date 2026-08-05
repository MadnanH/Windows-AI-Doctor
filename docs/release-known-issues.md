# v1.0 readiness known issues

## Release blockers

1. Production MSIX identity images and trusted certificate are external protected inputs; no signed artifact has been produced or trust-validated.
2. Native Windows 10 x64, Windows 11 x64, and Windows 11 ARM64 acceptance reports are absent.
3. Disposable-VM restore-point, destructive repair, restart, crash recovery, and rollback evidence is absent.
4. Manual keyboard, Narrator, high-contrast, 200% scaling, localization, and first-run evidence is absent.
5. Release-hardware provider and long-duration soak evidence is absent.
6. The exact Prompt 40 commit has not yet passed the remote CI release gate or final security review.

These are high or critical evidence gaps, so the candidate decision is **NO-GO**. They are not converted into source-code simulations.

## Non-blocking product limitations

- Monitoring and scheduled scans run only while WAID is open and cannot wake a closed application.
- Minidump analysis is metadata-focused and does not provide debugger symbol analysis.
- In-app notifications are local; email, SMS, remote push, and Action Center delivery are not implemented.
- Provider availability varies by Windows edition, permissions, firmware, hardware, and third-party security products. Unknown remains explicit.
- Portable workspace selection is command-line/marker based rather than a pre-start graphical picker.

The complete maintained limitation inventory remains in `WAID_PROGRESS.md` and must be reviewed for every release decision.
