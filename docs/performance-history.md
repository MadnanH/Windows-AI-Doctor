# Performance History and Trend Dashboard

WAID 0.22 stores local performance samples and versioned UTC rollups for CPU utilization, memory utilization, fixed-disk free space, network throughput, temperature, battery charge, and responsiveness latency.

## Metrics and quality

CPU, memory, and disk free space reuse the low-overhead Windows collector. Network throughput is a byte-per-second delta from active non-loopback interfaces. Battery percentage uses `GetSystemPowerStatus`. Responsiveness is an explicitly estimated scheduler wake-up delay. Temperature is recorded as unavailable because Windows has no uniform supported temperature API; missing data is never presented as healthy. Every sample carries measured, estimated, unavailable, or gap quality, a unit, timestamp, source, and optional safe detail.

## Aggregation

`performance-rollup-v1` creates hourly, daily, and weekly UTC buckets containing minimum, maximum, average, 95th percentile, valid sample count, coverage percentage, unit, and quality. UTC bucketing avoids DST ambiguity. Sustained-change analysis requires at least six rollups, a 15 percent change, and directional consistency in at least 70 percent of the recent half.

Charts query stored rollups and use deterministic bounded downsampling that preserves the widest-range point in each bucket. The accessible summary reports displayed points, missing/gap count, range, and unit. Period comparison is descriptive and never claims causation.

## Cache, retention, and privacy

Repository generation changes after every sample, rollup, or retention write; cached queries include that generation and are cleared after service writes. This prevents stale charts. Raw samples, rollups, and retention jobs are stored locally in schema 22. The repository supports independent raw and rollup retention cutoffs and records deletion counts and policy version.

Metrics contain numeric system measurements and safe provider names only. No process names, network addresses, personal file contents, browser data, credentials, tokens, or product keys are collected.

## Limitations

Collection is user-triggered from the dashboard in this milestone. Network throughput and CPU require a prior sample for a delta. Responsiveness is an estimate, disk represents free space rather than device latency, and temperature remains unavailable without a supported provider. Long-duration accuracy, battery variants, sleep transitions, and UI rendering still require real Windows hardware validation.
