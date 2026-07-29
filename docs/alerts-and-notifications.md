# Alerts and Notifications Policy

WAID alerts are local, evidence-backed guidance. An alert never executes, approves, or queues a repair and never requests administrator access. Repair actions remain behind the separate explicit approval and safety workflow.

## Alert model

Every alert includes a stable deduplication key, category, severity, rule identifier and version, title, plain-language message, internal action link, occurrence and escalation counts, lifecycle state, and bounded evidence with source references and timestamps. Categories cover hardware, Windows, drivers, security, performance, storage, network, and updates. Severities are Information, Advisory, Warning, and Critical.

Evidence is normalized before persistence. Secret-like fields and values are removed, user-profile paths are replaced with `%USERPROFILE%`, and evidence history is limited to the 20 latest observations with at most 50 safe values each. Only validated internal `waid://` action targets are accepted.

## Deduplication, cooldown, and escalation

The rule owns the deduplication key. Repeated observations update the existing unresolved alert instead of creating a new notification. WAID preserves the latest rule version, raises but never lowers severity, increments occurrences, and retains recent evidence. Delivery is suppressed during the configured cooldown. Escalation is a transparent repeat-count level; it does not fabricate a higher evidence severity or bypass repair approval.

## Lifecycle

Active alerts may be acknowledged, snoozed for a bounded interval, or resolved. Acknowledgement suppresses duplicate delivery. Snooze suppresses delivery until its UTC expiry and automatically returns the alert to Active on a later observation. Acknowledgement and snooze changes are written to the security audit trail without including evidence values.

## Quiet hours and channels

Quiet hours use local Windows clock time and support same-day or overnight ranges. Information, Advisory, and Warning deliveries are suppressed during quiet hours. Critical alerts may be delivered during quiet hours so urgent evidence is not hidden. Minimum severity and enabled-channel policy are also enforced.

Delivery channels are separate from alert rules and optional. Prompt 24 enables the local in-app channel; no email, SMS, cloud, or third-party delivery is enabled. Channel failures are stored as typed delivery outcomes and logged by exception type only. Delivery is not retried in a loop, and a channel failure does not discard alert state or interrupt monitoring.

## Notification Center

The Notification Center provides category, state, severity, and text filters; clear evidence and action references; functional internal navigation links; acknowledge and four-hour snooze actions; and settings for cooldown, quiet hours, repeat escalation, and the in-app channel. Settings and alert state survive application restart.

## Storage

Schema 25 continues to use the existing `alerts` table for alert state and adds `alert_deliveries` and singleton `alert_settings`. Delivery records contain channel, UTC attempt time, typed status, and safe detail. The monitoring rule `live-alert-1.0` currently creates performance and storage alerts from real warning or critical live samples.

## Limitations

Only the in-app channel is implemented. Notifications appear while WAID is open; there are no Windows toast, Action Center, email, SMS, or remote push notifications. Long-duration cooldown, local-time changes, sleep/wake, and assistive-technology behavior require the supported Windows hardware/VM validation matrix.
