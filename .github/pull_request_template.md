## Change

Describe the user-visible and architectural impact.

## Verification

- [ ] Restore, Release build, and all tests pass with zero warnings
- [ ] Critical-path, security, architecture, coverage, and quality-policy gates pass
- [ ] Tests use isolated temporary workspaces and deterministic time/synchronization; no flaky sleeps or automatic retries were added
- [ ] Knowledge-base validation passes
- [ ] No secrets, personal data, or fabricated certification evidence is included
- [ ] Repair changes preserve explicit approval, backup, restore-point, and rollback gates
- [ ] Accessibility and x64/ARM64 checks pass where applicable
- [ ] Destructive validation is not applicable or has separate disposable-VM and snapshot evidence; it was not run in ordinary CI
