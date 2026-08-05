# v1.0 readiness security and privacy review

## Automated evidence

- Repair execution remains behind registration, policy, simulation, explicit approval, safety scoring, elevation-at-execution, backup/restore-point handling, validation, audit, and rollback controls.
- Plugins validate containment, compatibility, declared permissions, dependencies, hashes, and optional Authenticode before loading; plugin isolation is not represented as an OS sandbox.
- Imported case packages are authenticated, bounded, review-only, never executed, and never merged into local state.
- Diagnostic exports and logs use established redaction; evidence graph, chat, and reports preserve provenance without collecting browser history, passwords, personal file contents, or product keys.
- CI contains secret scanning, dependency vulnerability audit, security/critical tests, architecture gates, and a required aggregate release gate.
- Signing consumes an external certificate, validates its exact subject, and verifies the resulting signature. Credentials are not accepted from repository paths.

## Prompt 40 defect correction

The manual validation report previously persisted the complete application executable path. Schema 2 records only the executable filename and SHA-256. The release harness also rejects sensitive field names and expanded user-profile paths in supplied evidence.

## Required protected review

Before `GO`, archive an exact-commit `SecurityReview` evidence record confirming dependency and secret scans, signed-package trust, export/log samples, enterprise-policy boundaries, plugin rejection fixtures, case-package limits, standard-user behavior, and repair approval/elevation/audit behavior. A local administrator can alter local policy and append-only logs; this documented trust boundary is unchanged.
