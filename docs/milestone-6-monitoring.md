# Milestone 6: Continuous Monitoring and Intelligent Reporting

WAID now uses one `ScanCoordinator` for dashboard, monitoring, and scheduled scans. This prevents overlapping work and keeps monitoring read-only. Monitoring starts only by user action, lives only for the application process, supports cancellation, pauses for battery saver or sustained system load, and stores scored snapshots in SQLite. The schedule loop checks once per minute while WAID is open and supports daily, weekly, or custom intervals, missed-run detection, plugged-in and idle constraints, and active-scan skipping.

The Monitoring & Reports workspace provides live component state, active alerts, critical events, manual refresh, monitoring controls, startup launch, schedule controls, read-only crash analysis, inspectable evidence, prioritized repair plans, and HTML/JSON/ZIP exports. Missing providers are shown as unavailable rather than healthy.

Minidump handling opens dump files read-only with shared access, validates the minidump signature, reads timestamp, exception/bug-check metadata and module names, groups repeated crashes, calculates weekly frequency, and maps known bug checks to the embedded offline knowledge base. It never modifies or deletes dumps.

Evidence collection starts only from detected findings, uses an explicit metadata allow-list, timestamps each item, and removes secret-bearing fields and user-profile paths. Reports repeat redaction defensively across the complete serialized object graph. Browser data, credentials, product keys, tokens, personal-file contents, and unnecessary device identifiers are not collected.

Repair planning considers severity, confidence, correlated evidence, repair safety, rollback support, dependency order, expected impact, administrator requirements, and restart requirements. Dialogs show planned actions and safeguards. Medium, high, and critical repairs require an explicit acknowledgement checkbox. Approval is audited separately, then execution still passes through `RepairExecutor`; monitoring and scheduling have no path to automatic repairs.

PDF output is an explicit `IPdfReportExporter` boundary for a later renderer. No fake PDF file is emitted. The implemented production formats are HTML, JSON, and ZIP.
