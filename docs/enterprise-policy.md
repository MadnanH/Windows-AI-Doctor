# Enterprise Policy Mode

WAID resolves policy offline from a deterministic provider chain: built-in safe defaults (priority 0), then the administrator-deployed organization document (priority 100). Higher priority wins per capability. Organization decisions are locked and cannot be changed by user settings, profiles, plugins, or portable mode. Every effective decision shows its source and explanation on **Enterprise Policy Status**.

## Deployment

Deploy `enterprise-policy.json` to `%PROGRAMDATA%\Windows AI Doctor\enterprise-policy.json` with read access for WAID users and write access limited to administrators/deployment tooling. The same protected path is evaluated before installed and portable startup, so portable mode cannot bypass policy. Use [the schema](schemas/enterprise-policy-v1.schema.json) and [sample](../deployment/enterprise-policy.sample.json). Do not store credentials, tokens, tenant secrets, or personal data in policy.

Supported capability keys are `cloudServices`, `aiFeatures`, `repairs`, `exports`, `plugins`, `monitoring`, `portableMode`, and `diagnostics`. Omitted keys retain the built-in allowed default. `false` blocks the capability. Retention values are maximum local retention in days for diagnostics/logs, audit, and monitoring.

Policy is limited to one MiB, rejects unknown fields and unsupported versions, and validates identifiers and retention bounds. Invalid, unreadable, or malformed organization policy fails closed for all governed capabilities, records a typed `WAID-POLICY-INVALID` snapshot, logs safely, and writes an audit event. It never silently falls back to a permissive policy.

## Enforcement

Policy is checked before plugin assembly loading, portable workspace creation, diagnostic scanning, AI requests, repair simulation/execution, plugin installation/enable, diagnostic export, and monitoring start/cycles. Startup applies restrictive diagnostic/audit retention ceilings, and monitoring applies its retention ceiling. Safe actions such as disabling/quarantining plugins, deleting conversations, viewing status, and correcting deployment remain available.

Effective snapshots, fingerprints, rules, source/lock state, retention, failures, updates, and rollbacks persist in SQLite schema 33 without secrets. Refresh detects file updates. Rollback selects only a previously validated snapshot and is reserved for administrative deployment/recovery tooling; ordinary UI cannot invoke it. It is an operational recovery aid, not a replacement for correcting the deployed file, which will be evaluated again on refresh/restart.

## Administrative diagnostics

The Policy Status page displays active/fail-closed/rolled-back state, capability decisions, sources, explanations, lock status, retention, SHA-256-derived fingerprint, recent evaluations, and refresh. Rollback is available only through the administrative service/deployment workflow and is not exposed as a user control. Security-sensitive changes are appended to Logs & Audit.