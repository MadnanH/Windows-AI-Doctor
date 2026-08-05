# WAID Development Progress

Last updated: 2026-08-05

Current version: **0.33.0-dev**

Active branch: `main`

## Completed modules

- Clean Architecture .NET 8/WinUI 3 solution, MVVM, dependency injection, SQLite schema v31, Serilog, offline diagnosis, 18 production scanners, monitoring, scheduling, crash analysis, evidence collection, repair approval, rollback, and repair history.
- Scanner execution policies with validated metadata, prerequisites and dependencies, bounded parallelism, configurable timeouts, one bounded read-only retry, and explicit lifecycle states. Failures are isolated, completed cancellation results are retained, and unavailable data is never inferred as healthy.
- Version 2 knowledge documents with startup/build validation, required-field and duplicate checks, repair-map validation, unsupported-version rejection, and deterministic legacy-array migration.
- Manifest-based plugin loading with API/host compatibility checks, collectible dependency contexts, publisher allow-list, optional Authenticode enforcement, persistent disabled state, failure quarantine diagnostics, service-registration isolation, and a reachable Plugins page.
- HTML, JSON, ZIP, and real local PDF reports. PDFsharp 6.2.4 (MIT) produces paginated, versioned reports with the same sensitive-value exclusions and all diagnostic sections.
- Explicit destructive-VM authorization guard requiring administrator access, disposable-VM and snapshot acknowledgements, a separate confirmation, and an exact confirmation phrase. Normal mode cannot authorize it.
- Navigation automation identifiers, keyboard-native controls, theme/high-contrast-friendly UI, scalable text, a Plugins navigation target, PDF export control, and an automated all-page navigation/XAML smoke script.
- Evidence-producing Windows 10 x64, Windows 11 x64, Windows 11 ARM64, administrator, standard-user, offline, and unsupported-hardware manual validation scripts. Reports record OS edition/version/build, architecture, app version, scanner statuses, permission failures, safeguards, UI launch behavior, UTC time, and pass criteria.
- GitHub Actions for restore, warning-free Release build, tests, knowledge validation, accessibility smoke, vulnerability audit, secret scanning, x64 publish, and ARM64 compile; Dependabot, security policy, and PR checklist.
- MSIX manifest template and documented x64/ARM64 identity, upgrade, uninstall/data-retention, and protected signing path. No certificate, secret, or fabricated signing material is committed.
- Commercial hardening Prompt 01 architecture baseline: verified current/target architecture, complete dependency and feature audit, persistence ownership/schema inventory, explicit risk register, incremental migration plan, README links, dependency-rule tests, and expanded service-registration smoke coverage.
- Commercial hardening Prompt 02 composition root: modular feature registration, versioned non-secret host options, replaceable Windows adapters, host-owned Serilog provider, typed actionable startup failures, validated required services/lifetimes/duplicate scanner and repair IDs, module diagnostics, recovery UI, and service-locator regression enforcement.
- Commercial hardening Prompt 03 diagnostics foundation: local structured JSON logging, typed event taxonomy, asynchronous correlation and operation context, redacted append-only daily audit records, bounded retention, searchable Logs & Audit UI, sanitized support export, and non-fatal diagnostic-storage failures.
- Commercial hardening Prompt 04 persistence reliability: ordered transactional schema migrations 1ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ7, upgrade backups, WAL recovery, integrity/foreign-key checks, newer-schema rejection, bounded verified backups, explicitly approved recovery, typed maintenance failures, audit events, and Settings database-health UI.
- Commercial hardening Prompt 05 unified configuration: deterministic default/machine/user/profile/session/policy precedence, immutable operation snapshots, enforced policy locks, fail-closed feature flags, schema-8 legacy migration, privacy-safe profile import/export, reset/audit behavior, and categorized searchable Settings UI.
- Commercial hardening Prompt 06 scanner framework: stable backward-compatible scanner metadata/output contracts, dependency planning, prerequisite skips, bounded parallel execution, detailed progress/cancellation, evidence redaction, schema-9 transactional provenance, resource estimates, and live Dashboard plan states.
- Commercial hardening Prompt 07 driver conflict analyzer: normalized privacy-safe driver inventory, signature/problem/disabled/failed/duplicate/orphan/incompatibility/change rules, conservative event correlation, schema-10 report history, typed failures, standard-user uncertainty, and a reachable filtered Driver Health UI with evidence and device history.
- Commercial hardening Prompt 08 startup and boot analyzer: normalized multi-source startup inventory, quoted command parsing, deduplication, boot-duration and service-failure correlation, separate security/performance concerns, critical-entry protection, reversible action simulation, schema-11 history, and a reachable Boot Health UI.
- Commercial hardening Prompt 09 Windows Update intelligence: normalized update attempts and KB/error codes, offline error mapping, network/servicing/policy/storage/reboot/service cause separation, ordered supported repair prerequisites, explicit-approval simulation, schema-12 history/outcome storage, and a reachable Update Health timeline.
- Commercial hardening Prompt 10 Storage Health Center: separate privacy-safe disk/volume/filesystem models, cautious SMART/temperature/wear/latency thresholds, space and filesystem evidence, snapshot trends, bounded cleanup dry-runs, cancellable folder analysis, schema-13 storage history/exclusions, and a reachable Storage Health dashboard.
- Commercial hardening Prompt 11 Windows Security Posture Analyzer: capability-aware Defender/firewall/Secure Boot/TPM/encryption/Core Isolation/UAC/SmartScreen/Credential Guard/update checks, explicit unknown states, policy locks, prerequisite remediation previews, schema-14 acknowledgements, and a reachable Security Center.
- Commercial hardening Prompt 12 Network Diagnostic Center: adapter/IP/DNS/route/gateway/proxy/VPN/Wi-Fi/firewall/service evidence, user-selected bounded probes, failing-layer classification, privacy-safe schema-16 history and export, cancellation, reset-evidence gating, and a reachable Network Health page.
- Commercial hardening Prompt 13 AI Chat Assistant Core: offline retrieval across scans, diagnosis, and repair history; separated prompt/provider/safety services; citation enforcement; hostile-evidence neutralization; timeout fallback; schema-15 conversation lifecycle; and a reachable evidence-aware AI Chat page.
- Commercial hardening Prompt 14 explainable diagnosis: immutable schema-1.0 explanations with weighted evidence, counter-evidence, alternatives, deterministic calibrated confidence, version metadata, change-over-time, explicit unsupported results, and consistent dashboard, diagnosis, history, repair-preview, chat, HTML, and PDF rendering.
- Commercial hardening Prompt 15 evidence aggregation: immutable schema-1.0 graph nodes, provenance-preserving deduplication, freshness and retention, versioned temporal/conflict/repair/cross-domain candidate strategies, cycle prevention, schema-17 transactional persistence, and an accessible filtered Incident & Evidence Explorer.
- Commercial hardening Prompt 16 offline knowledge retrieval: curated versioned articles with errors, symptoms, prerequisites, risks, OS compatibility, references, source/license/trust/checksum metadata; relevance/evidence/compatibility/freshness ranking; atomic local index rebuild; hostile-content rejection; repair-trust isolation; grounded-chat citations; and an accessible Knowledge page.
- Commercial hardening Prompt 17 repair recommendation ranking: registered-repair-only candidates, deterministic v2 factor scoring, policy/conflict/prerequisite rejection, reversibility and downtime metadata, bounded outcome feedback, high-risk auto-selection prohibition, schema-18 run/decision persistence, and expanded comparison/simulation UI.
- Commercial hardening Prompt 18 predictive health: conservative versioned linear-trend analysis for storage wear, crash rate, temperature, memory instability, update failures, and performance decline; minimum-history, noise, drift, and abrupt-change guards; confidence/risk ranges; holdout validation; schema-19 persistence; and a reachable local Predictive Health dashboard.
- Commercial hardening Prompt 19 live monitoring: separate real Windows signal collection, aggregation, alerts, policy gating, and budget enforcement; adaptive intervals; battery/load/gap/failure awareness; restart recovery; bounded schema-20 history; full disable/pause/resume controls; and a reachable privacy-explained Live Monitoring page.
- Commercial hardening Prompt 20 reliability timeline: immutable-source projection across crashes, updates, driver changes, repairs, alerts, health shifts, reliability findings, and retention gaps; stable UTC IDs/order; versioned possible-association grouping; schema-21 paging/indexes; provenance/evidence/comparison/export; and accessible timeline/table modes.
- Commercial hardening Prompt 21 performance history: validated CPU/memory/disk/network/temperature/battery/responsiveness samples with explicit quality; versioned UTC hourly/daily/weekly rollups; deterministic spike-preserving downsampling; sustained-change and period comparison; generation-safe caching; schema-22 retention jobs; and an accessible cards/chart dashboard.
- Commercial hardening Prompt 22 digital twin snapshots: privacy-safe versioned component capture, partial-failure isolation, canonical SHA-256 integrity, compressed schema-23 persistence, pin-aware retention, deterministic risk-highlighted diffs, repair-linked pre/post snapshots, and a reachable comparison/export page.
- Commercial hardening Prompt 23 scheduled scanning: daily/weekly/monthly/custom/startup/idle/maintenance recurrence, DST-safe UTC next runs, sleep/wake catch-up, power/idle/network/load/window deferrals, two-layer overlap prevention, typed/audited outcomes, schema-24 history, and an accessible editor, preview, run-now, pause, and history UI.
- Commercial hardening Prompt 24 alerts and notifications: categorized versioned evidence alerts, privacy-safe deduplication, cooldown, quiet hours, acknowledgement, bounded snooze, repeat escalation, optional policy-aware delivery channels, typed delivery outcomes, schema-25 restart persistence, audit events, live-monitoring integration, and a reachable filtered Notification Center.
- Commercial hardening Prompt 25 repair orchestration: durable typed lifecycle and state machine, dependency/conflict assessment, read-only simulation, centralized approval and policy gates, bounded execution and cancellation, validation, rollback outcomes, startup crash recovery, schema-26 persistence, complete decision auditing, and a reachable Repair Lifecycle page.
- Commercial hardening Prompt 26 repair simulation: read-only versioned dry runs, deterministic canonical fingerprints, exact/estimated/unknown typed effects, assumptions and warnings, expiry and changed-definition invalidation, schema-27 metadata, production module declarations, and an expanded before/after Repair Lifecycle preview.
- Commercial hardening Prompt 27 recovery system: capability-tiered restore points and resource artifacts, preflight space and ACL enforcement, SHA-256 item and manifest integrity, idempotent creation, verified rollback outcomes, schema-28 persistence, root-confined expiry cleanup, audited explicit rollback, and recovery-readiness UI.
- Commercial hardening Prompt 28 repair safety scoring: deterministic versioned impact/reversibility/privilege/downtime/data/uncertainty/validation factors, fixed weighted rubric, non-weakenable confirmation thresholds, restrictive policy precedence, schema-29 decisions and exceptions, audited revalidation, and consistent factor UI.
- Commercial hardening Prompt 29 repair workflow UI: accessible end-to-end plan, evidence, simulation, approval, progress, restart, validation, rollback, recovery, and report surfaces; persisted UI-safe summaries, exact-action announcements, duplicate-command suppression, selection guards, and safe approval-stage restart resume.
- Commercial hardening Prompt 30 repair audit and outcomes: immutable decision/evidence/approval/execution/validation/rollback/feedback chain, validation-based outcome classification, rebuildable bounded explainable aggregates, schema-30 persistence, privacy-safe export, and accessible filtered Repair History.
- Commercial hardening Prompt 31 Technician Dashboard: optional profile-based professional workspace composed from real scans, incidents, evidence, repairs, and reports; compact/detailed layouts, saved filters, separately persisted case notes/metadata, bounded data, accessible navigation, and unchanged simple home mode.
- Commercial hardening Prompt 32 Portable Edition: pre-DI workspace resolution, isolated compatible schema-31 storage, write/permission probing, typed read-only failures, per-workspace process and cross-process locking, coexistence, marker-confined approved cleanup, portable publishing, and visible mode/path indicator.

## Fully working features

- Offline Scan -> Diagnose -> Recommend -> Confirm -> Repair -> Verify flow; no repair executes silently or from monitoring.
- SQLite persistence for scans, diagnosis, repair history, schedules, approvals, settings, and health snapshots.
- Scanner cancellation/failure isolation and user-visible degraded data states; unavailable data is never inferred as healthy.
- Plugin failures cannot terminate startup; rejected contexts unload, while active plugin unload correctly requires restart because registered services may retain plugin types.
- Redacted local HTML, JSON, ZIP, and PDF export, including version, date, system summary, scores, findings, evidence, root causes, confidence, repair ordering, history, limitations, redaction notice, and PDF page numbers.
- Searchable local technical logs and security audit history with expandable sanitized detail; repair requests, approval decisions, outcomes, and rollback activity carry correlation and operation identifiers.
- Migration-safe SQLite initialization and database maintenance with preserved upgrade data, per-step rollback, concurrency serialization, verified online backup/restore, and visible schema, journal, backup, and migration status.
- Versioned local configuration with policy enforcement, source attribution, session isolation, validated machine/policy files, profile acknowledgement for experimental changes, and safe defaults that prevent invalid configuration from enabling experimental flags.
- Reproducible scan sessions persist scanner/framework versions, execution state, attempts, duration, failure codes, sanitized observations, findings, and approximate resource usage in a single transaction; one scanner failure does not invalidate independent results.
- Driver Health runs entirely offline and read-only, persists normalized inventories and reports, cites evidence and uncertainty for every finding, and never downloads or changes drivers.
- Boot Health inventories startup sources and boot evidence without changing state, protects critical Windows entries, explains measured versus estimated impact, and previews explicit-approval and rollback requirements.
- Update Health correlates local history, services, policy, reboot markers, DISM/CBS signals, events, and storage; every proposed remedy declares prerequisites, elevation, restart, and approval while forbidding silent update removal.
- Storage Health unifies capacity, reliability, filesystem, trend, cleanup, and folder-size evidence while never reading file contents or deleting files; every cleanup category requires explicit selection.
- Security Posture complements Windows Security with transparent evidence and non-alarmist findings; unavailable/unknown states are never labeled disabled and policy-managed controls are never bypassed.
- AI Chat works offline, cites persisted WAID evidence, exposes provider/model/confidence state, rejects uncited output, falls back safely after provider failure, and cannot execute repairs.
- Network Health runs read-only local inspection plus optional user-selected bounded probes, separates failure layers, persists latency/loss/evidence, and never performs a reset directly.
- Every current diagnosis now carries a supported or explicitly unsupported explanation. Identical evidence and rule versions reproduce confidence; explanations persist with evidence links, alternatives, calibration metadata, and version provenance in existing schema-16 diagnosis JSON.
- Evidence Explorer aggregates real persisted scanner findings and repair outcomes, deduplicates without changing raw records, preserves provenance, exposes freshness and filters, and labels every derived edge as an association or causal candidate rather than causation.
- Offline Knowledge works without network access, ranks curated guidance against search terms, saved finding codes, Windows compatibility, trust, and freshness, and prevents community/plugin material from defining repair guidance.
- Repair Plan recommends only active registered modules, persists every eligible/rejected decision and factor set, uses bounded repair outcomes without learning new mappings, and never marks high-risk work for automatic selection.
- Predictive Health analyzes only saved local health snapshots, suppresses weak or unstable signals, versions every model and feature, preserves source references and uncertainty, and never triggers repairs.
- Live Monitoring is opt-in and fully disableable, samples real CPU/memory/storage signals within a two-second/16-sample default cycle budget, adapts its interval when stable, pauses for battery saver/high load/policy/user requests, isolates collector failures, records gaps and restart recovery, bounds retention, and never executes repairs.
- Reliability Timeline rebuilds a deterministic local projection without changing source records, preserves visible provenance, represents retention gaps, provides stable indexed paging/filtering, labels incident groups as possible associations rather than causation, and exports redacted projected evidence.
- Performance History stores real or explicitly unavailable local metrics, builds versioned UTC rollups that match chart queries, preserves quality/coverage, invalidates caches on storage changes, bounds long histories, summarizes charts accessibly, and compares periods without causal claims.
- Digital Twin captures structured local system state without personal-file inventories or secret material, tolerates unavailable providers, preserves serializer and integrity versions, stores compressed snapshots, compares stable field changes, and links pinned pre/post repair evidence without weakening approval gates.
- Scheduled Scanning reuses the read-only scan coordinator, persists next-run and policy state, handles missed runs once after wake, records every decision and important audit event, defers under unsafe resource conditions, prevents duplicate scans, and never invokes repair execution.
- Alerts merge repeated observations by stable rule key, preserve bounded redacted evidence and rule versions, suppress spam through cooldown/quiet/snooze/acknowledgement state, isolate channel failures, persist delivery history and policy, navigate to related WAID evidence, and never authorize or execute repairs.
- Every queued or interactive repair now passes through the same persisted orchestration lifecycle. Plans are simulated before approval, high-risk acknowledgement is explicit, execution is serialized and bounded, validation is recorded, rollback outcomes are visible, interrupted work never auto-runs, and all significant decisions are audited.
- Repair dry runs never call executors or request elevation, canonicalize and fingerprint the current definition, label every effect exact, estimated, or unknown, persist their validity window and assumptions, and are revalidated immediately before execution so stale previews fail closed.
- Backup-required repairs execute only with a protected, complete, hash-validated artifact. Recovery capability is explicit, standalone rollback requires confirmation, file/directory restoration is hash-verified, registry provider success is recorded, outcomes are audited, and expiry cleanup cannot escape the managed root.
- Every simulated repair receives a reproducible 0–100 safety score with seven explained weighted factors. Baseline confirmation derives from both score and declared safety; policy can only block or strengthen it. Score/policy changes invalidate approval, and High/Critical repairs cannot bypass acknowledgement.

- Repair History now presents filtered immutable lifecycle evidence, approval actor, execution and validation outcomes, rollback details, redacted export, and rebuildable descriptive outcome statistics that cannot authorize or generate repair behavior.

- Portable mode confines application-owned writable paths to the selected workspace and preserves all repair safeguards; installed mode remains unchanged.

## Partially working features

- Plugin enable/disable persistence is implemented in the service layer; the Plugins page currently presents state and diagnostics, while changing state is performed by administration tooling and takes effect after restart.
- Feature-flag resolution and UI are working; the cloud-provider flag is intentionally reserved and has no network provider implementation.
- Automated accessibility checks validate navigation targets, identifiers, and XAML parseability; screen-reader, focus, high-contrast, and 200% scaling acceptance still requires archived manual evidence.
- Packaging inputs and installed/portable publish paths are ready, but production MSIX signing and store/update distribution require protected release credentials and a release environment.

## Features requiring real-hardware testing

- Windows 10 x64 launch/scanners; Windows 11 x64 launch/scanners; Windows 11 ARM64 launch/scanners.
- Administrator and standard-user repair safeguards on disposable, snapshotted VMs.
- Offline behavior, unsupported-provider behavior, SMART/NVMe, GPU, battery, Defender, Reliability Monitor, and minidump provider variations.
- Keyboard, focus, screen-reader, high-contrast, and text-scaling manual acceptance.
- Restore point, backup, rollback, simulated interruption, restart-required repair, and approval-flow destructive validation.
- Network adapters, Wi-Fi providers, VPN clients, proxy policies, captive portals, ICMP filtering, and firewall/service variations on supported Windows hardware.

No platform certification is claimed until a matching passing JSON report from `scripts/Run-WaidManualValidation.ps1` is archived. No such report is committed for this milestone.

## Remaining modules

- Execute and archive the platform/hardware/accessibility/destructive validation matrix in supported disposable VMs and physical hardware.
- Production certificate-backed MSIX signing, distribution, upgrade, and uninstall validation.
- UI controls for changing persisted plugin enable/disable state and trusted-publisher policy.
- Detailed multi-report diagnosis and repair-event history drill-down.
- Implement repositories for remaining schema-31 reserved tables only as their owning commercial-hardening prompts add real use cases; no disconnected or speculative repository APIs were added.

## Known limitations

- Scheduled monitoring remains in-process and cannot wake a closed application.
- Portable workspace selection is command-line/marker driven in this milestone; the recovery UI provides actionable permission failures, while a graphical folder picker before DI startup remains future UX work. Removable-media disconnect, filesystem ACL variants, and cross-session mutex behavior require real-Windows validation.
- Scanner APIs and hardware providers vary by Windows edition, permissions, architecture, and hardware; explicit degraded results are not proof of a healthy subsystem.
- Active plugins cannot safely unload until restart after registering services; rejected or failed collectible contexts are unloaded immediately.
- Authenticode verification is optional policy and is not enabled without an organization-approved signing policy.
- Minidump analysis remains read-only and metadata-focused; it is not a symbol debugger.
- Diagnostic packages should still be reviewed before external sharing despite defense-in-depth redaction.
- Append-only audit files are not cryptographically signed and can be altered by a local administrator. Audit-storage failure is reported and logged without crashing or silently changing a repair outcome.
- Automated database tests cover fresh installs, schema 1 and 6 upgrades, interrupted migration, concurrent initialization, corruption, newer schemas, backup, restore, and retention. Live Settings recovery UI still requires manual Windows acceptance.
- Machine and policy enforcement depends on deployment applying administrator-only ACLs to `%PROGRAMDATA%\Windows AI Doctor`; local administrators can change those files. Search, policy-lock, profile, and experimental-warning UI still require manual accessibility acceptance.
- Scanner CPU and managed-memory deltas are process-level diagnostic estimates during parallel execution, not per-thread accounting. Provider-specific progress remains coarse until an individual scanner reports detailed stages.
- Driver signature state comes from Windows signed-driver catalog metadata and is not a malware verdict. Driver-event attribution is intentionally limited to events whose sanitized text matches a known device; physical-device and OS-version acceptance remains outstanding.
- Startup impact is often correlated from overall boot events because Windows does not expose uniform per-entry timing. Disable/rollback is deliberately simulation-only in Prompt 08; no startup state is modified.
- Windows Update history, event retention, DISM availability, and organization-managed policy vary by environment. The offline error catalog is intentionally bounded; unknown codes stay unknown and repair execution remains behind the established approval workflow.
- Storage reliability counters and SMART semantics vary by device, controller, bridge, firmware, and vendor. Missing temperature/wear is unavailable rather than healthy; cleanup and folder analysis are read-only estimates.
- Security provider availability varies by Windows edition, hardware, firmware, virtualization, third-party antivirus, management policy, and permission. Unknown/unavailable remains explicit and requires real-device validation.
- Chat retrieval is intentionally bounded to recent persisted evidence. The deterministic offline provider summarizes evidence but is not a general-purpose language model; no cloud AI is enabled.
- Network probes cannot prove general internet reachability: ICMP may be filtered, captive portals and proxies vary, and optional DNS/HTTP checks run only against user-supplied targets. Wi-Fi signal/profile details may be unavailable from Windows providers.
- Explainable diagnosis is bounded by the offline rule catalog. Confidence is deterministic evidence strength, not certainty or repair-success probability; older reports surface explicit legacy/unsupported explanation metadata.
- Evidence graph relationships are deterministic candidate associations based on normalized code, attributes, and time. They cannot establish physical or software causation; source timestamp quality and scanner coverage limit correlation quality.
- The embedded knowledge catalog is intentionally bounded and updated only with application releases. It is not a complete replacement for vendor documentation; compatibility ranking relies on declared article metadata and the local OS version.
- Repair ranking estimates expected benefit and downtime from deterministic metadata, not measured guarantees. Feedback is capped and cannot override registration, policy, prerequisites, conflicts, approval, or executor safety gates.
- Predictive Health requires at least five observations spanning three days. Its ranges are statistical uncertainty, not failure probability; provider gaps, irregular workloads, hardware changes, and short history can suppress or distort trends, and long-duration real-hardware calibration remains outstanding.
- Live Monitoring runs only while WAID is open and cannot wake a closed application. Sleep is inferred from scheduling discontinuities; Windows power-transition behavior, provider accuracy, and long-duration resource budgets still require real-hardware validation.
- Reliability Timeline coverage depends on retained source records. Grouping is a deterministic 30-minute/subject heuristic and never proves causation; health shifts use a fixed ten-point threshold, projection rebuild is user-triggered, and long-duration UI/provider validation remains outstanding.
- Performance metric collection is user-triggered. CPU/network need a prior delta; responsiveness is estimated; disk currently represents free space rather than latency; temperature is explicitly unavailable without a supported provider; and long-duration real-hardware accuracy remains outstanding.
- Digital Twin components may summarize the latest persisted WAID analyses, so completeness depends on prior scanner coverage and Windows provider availability. Diffs show association and change, not causation; real-hardware serializer coverage and long-duration retention remain outstanding.
- Scheduled scans run only while WAID is open and cannot wake or launch a closed application. Startup means application startup; network policy reflects interface availability rather than internet reachability; long-duration sleep, DST, idle, battery, and organization-policy behavior still require real-Windows validation.
- Alert delivery currently supports only the in-app channel while WAID is open; Windows toast/Action Center, email, SMS, and remote push are not implemented. Long-duration cooldown, local-time change, sleep/wake, and assistive-technology behavior still require real-Windows validation.
- Default repair validation confirms the typed executor result and treats restart-required results as provisional. Process or machine failure may prevent in-process rollback; WAID marks the durable record RecoveryRequired and never retries automatically. Restore-point, backup, cancellation, restart, and rollback behavior require disposable-VM validation.
- Dry-run effects describe declared repair behavior and cannot predict exact command output or every Windows side effect. Built-in duration is estimated, storage use may be unknown, previews expire after 15 minutes, and real behavior still requires disposable-VM validation.
- Registry rollback verification relies on successful Windows provider import rather than a complete post-import value comparison. Restore Point availability, ACL behavior, locked resources, junctions, disk-space races, interruptions, large directory trees, and rollback require disposable-VM and real-hardware validation.
- Safety weights are transparent conservative rules, not repair failure probabilities. The default policy preserves baseline gates but does not add enterprise policy-file deployment; organization-specific policy rollout and manual High/Critical UX validation remain outstanding.
- Repair outcome rates describe retained validation-backed observations, not future success probability. Aggregate rebuild is bounded to 10,000 audit entries; long-term retention calibration and cross-version cohort analysis remain outstanding.
- Automated repair-workflow accessibility checks cover parseability, stable automation IDs, live regions, and theme-resource use. Narrator, keyboard focus order, high contrast, 200% scaling, cancellation timing, and restart/rollback behavior still require disposable-VM validation.

## Build status

**Passing - Release, complete `WindowsAIDoctor.sln`.**

- Compiler warnings: 0
- Compiler errors: 0
- x64 self-contained publish: passing
- ARM64 compile: passing
- Last verified: 2026-08-05
- Runtime/platform certification: not claimed; archived manual evidence is required

## Test status

**Passing - 360/360 tests**

- `WAID.Domain.Tests`: 10 passed
- `WAID.Application.Tests`: 114 passed
- `WAID.Diagnosis.Tests`: 75 passed
- `WAID.Infrastructure.Tests`: 161 passed
- Failed: 0
- Skipped: 0
- Accessibility navigation smoke: passed



## Current version

**0.33.0-dev - Portable Edition**

Prompt 32 adds no-install portable publishing, isolated chosen-workspace storage, portable-safe configuration, permission/read-only detection, per-workspace single-instance locking, coexistence, marker-confined cleanup, and a visible portable indicator without weakening repair safeguards.

## Next milestone

**Commercial hardening Prompt 33 - Secure Plugin SDK and Certification**

Apply only Prompt 33 from the ordered commercial-grade prompt set. Preserve workspace isolation, plugin trust enforcement, explicit repair approval, audit immutability, privacy, and fail-closed recovery; restore/build/test independently and commit before continuing.

## Update procedure

Update completed, remaining, build/test status, version, limitations, and next milestone in the same commit as every milestone.
