# Logging, Diagnostics, and Audit Operations

WAID writes diagnostics only to the local application data directory by default. It does not transmit logs, audit events, or support exports. Technical logs are structured JSON and roll daily; the default retention is 14 days. Security-sensitive audit events are append-only JSON Lines files partitioned by UTC day, with a default retention of 365 days. Retention is configurable within validated bounds.

## Event taxonomy

| Range | Category | Current events |
| --- | --- | --- |
| 1000-1999 | Scanning | lifecycle, completion, degraded scanner result |
| 2000-2999 | Repairs and policy | request, approval or rejection, completion, rollback |
| 3000-3999 | Monitoring | monitoring lifecycle |
| 9000-9999 | Application safety | startup and composition failures |

Every top-level scan or repair operation creates an operation ID and correlation ID. Nested work inherits these values through an asynchronous operation context and structured logging scope. The operation ID identifies one execution; the correlation ID connects related work across components.

Audit events record UTC time, actor, action, target, result, repair risk, elevation requirement, rollback capability, operation ID, correlation ID, and a redacted detail. Repair requests, policy decisions, approvals, execution outcomes, and rollbacks are audited. The audit API exposes append and query operations only; it has no update method.

## Redaction and failure behavior

Before storage or export, WAID removes recognized passwords, tokens, product keys, authorization values, user-profile paths, and other sensitive fields through the shared report redactor. Do not put secrets in message templates or property names. Diagnostic storage failures are returned as typed results or degrade the logging module; they do not crash the application. Repair audit write failures are surfaced in the technical log but currently do not fail an otherwise safe repair transaction.

Daily retention removes complete expired files and never rewrites active audit records. Append-only storage protects application semantics, but a local administrator can still alter files; the current format is not cryptographically signed or tamper-evident.

## Operator workflow and support exports

Open **Logs & Audit** to search technical messages and security audit events. Expand an item for its sanitized technical detail. Use **Export sanitized diagnostics** to create a local JSON support export containing bounded log and audit results plus a redaction notice.

Review every export before sharing it. Share the sanitized export rather than raw files from the Logs or Audit directories. WAID intentionally excludes browser history, document contents, passwords, tokens, and product keys, but no automated redaction system can guarantee that arbitrary third-party plugin text is safe.
