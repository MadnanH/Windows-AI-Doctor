# Windows AI Doctor v1.0 readiness validation

This source snapshot is an internal validation candidate, version `0.40.0-dev`. It is **not an approved v1.0 release**.

The candidate includes offline Windows diagnostics, explainable correlation, health scoring, evidence and history, safe explicitly approved repair orchestration, SQLite persistence, monitoring and scheduling while the app is active, reports, enterprise policy, portable mode, plugins, CLI automation, encrypted case exchange, and x64/ARM64 release publishing.

Prompt 40 adds a frozen release manifest, reproducible automatic validation, fail-closed external-evidence evaluation, an explicit go/no-go record, and the associated checklist, matrix, security review, known issues, and release notes. It also removes the full executable path from future manual-validation evidence.

Current decision: **NO-GO**. Automated build, tests, and artifact checks pass, but production signing, exact-commit CI, real Windows/ARM64, accessibility, destructive recovery, hardware-provider, security-review, and long-soak evidence remain required. Development artifacts are unsigned and visibly labeled as such.
