# Live Monitoring Service

WAID 0.20 provides optional low-overhead health sampling while the desktop application is running. It is disabled until the user enables and starts it, and it can be paused, resumed, or completely stopped from the Live Monitoring page. Monitoring never invokes the repair framework.

## Signals and sources

- CPU busy percentage from Windows `GetSystemTimes`; the first sample is unavailable because a safe delta requires two readings.
- Physical memory load from Windows `GlobalMemoryStatusEx`.
- Lowest free-space percentage across ready fixed drives from .NET `DriveInfo`. Drive names and file contents are not stored.

Collectors, aggregation/threshold classification, alert evaluation, persistence, and resource-budget enforcement are separate services. A failed collector is isolated and recorded with a safe error code; other collectors can continue.

## Intervals and adaptive sampling

The base interval is configurable from 15 seconds to one hour. When severity states remain unchanged, the service progressively increases the interval up to the configured maximum (four hours maximum). Battery saver, system load at or above 80 percent by default, user pause, and policy denial stop collection. Long scheduling discontinuities are recorded as gaps so sleep or application scheduling is never represented as continuous coverage.

## Resource budget

A collection cycle defaults to a two-second wall-clock budget and at most 16 samples. A timed-out collector is cancelled and the gap is recorded. The UI shows the most recent duration, budget, sample count, collector-failure count, and adaptive interval. These are enforcement and diagnostic limits, not a guarantee about third-party operating-system providers.

## Retention and restart recovery

Samples, sessions, gaps, failures, and retention state are stored locally in SQLite. Defaults retain 30 days and at most 50,000 signal samples. Retention runs after each successful cycle. An open session found at the next start is closed with an application-restart reason before a new session begins.

## Privacy and limitations

No browser data, credentials, product keys, personal document contents, or file contents are collected. Numeric measurements, timestamps, severity, safe provider names, and bounded diagnostics remain local. Live monitoring runs only while WAID is open and cannot wake a closed application. Sleep awareness uses elapsed scheduling gaps in this milestone; real power-transition and long-duration budget validation still require supported Windows hardware.
