# Scheduled Scanning and Maintenance Windows

WAID scheduled scans reuse the normal read-only scan coordinator. A scheduled run cannot execute repairs, elevate privileges, or bypass scanner safety policies.

## Schedule types

The scheduler supports daily, weekly, monthly, custom interval, application startup, system-idle, and maintenance-window schedules. Daily, weekly, monthly, and maintenance times use the Windows local time zone. Recurrence calculation converts the selected local occurrence to UTC, advances invalid spring-forward times to the next valid hour, and preserves a single unambiguous UTC next-run value across sleep and restart.

A monthly day that does not exist in a month uses that month's final day. Custom intervals are limited to 15 minutes through 30 days. Startup schedules run at most once per WAID application session. Maintenance windows may cross midnight.

## Execution policy

Before scanning, WAID checks whether scheduling is enabled or paused, whether a deferral remains active, AC-power and idle requirements, optional network availability, maximum system load, maintenance-window membership, and existing scan activity. Invalid or unavailable load data fails closed. Failed conditions create a typed 15-minute deferral instead of a tight retry loop.

Both the scheduler and shared scan coordinator prevent overlap. Every evaluation has a typed result such as completed, not due, deferred, overlap prevented, cancelled, or failed. Important execution and duplicate-prevention decisions are also written to the security audit trail. No elevation is requested because scheduled scans are non-privileged.

## Persistence and recovery

The schedule JSON retains the calculated next run, last run, deferred-until time, policy source, pause state, and policy settings. Schema 24 adds append-only scheduled-scan history containing the evaluation time, start/completion times, outcome, sanitized reason, scan session identifier, policy source, and next-run value.

If Windows sleeps past a due time, the first evaluation after WAID resumes runs the missed scan once, subject to current policy. The subsequent next run is calculated from completion so duplicate catch-up scans are not created.

## User interface

Monitoring & Reports includes an accessible schedule editor, preview, enable/pause controls, recurrence fields, power/idle/network/load policy, Save Schedule, Run Now, and recent decision history. Run Now still respects resource and overlap policy.

## Limitations

The scheduler runs in process while WAID is open. It does not install a Windows Task Scheduler task and cannot wake or launch a closed application. Startup means WAID application startup, and idle detection uses the supported Windows last-input API. Network availability reports local Windows interface state, not guaranteed internet reachability. Long-duration sleep, DST, battery, and organization-policy behavior still requires the supported Windows hardware/VM validation matrix.
