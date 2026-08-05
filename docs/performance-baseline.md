# WAID performance baseline

Version: 0.39.0-dev
Recorded: 2026-08-06
Configuration: Release, .NET SDK 8.0.419, Windows x64 development workstation

## Budgets

| Area | Budget | Measurement boundary |
|---|---:|---|
| Startup | 4,000 ms | Process initialization through interactive main-window activation |
| Memory | 350 MiB | Desktop steady-state working set after startup and one scan |
| CPU | 5% average | Enabled monitoring while otherwise idle |
| Scan | 120,000 ms | Complete read-only scan, excluding a provider reaching its declared timeout |
| Database | 250 ms P95 | Indexed interactive query against a retained representative dataset |
| Report | 5,000 ms | Local HTML/JSON report from a representative large diagnosis |
| Monitoring | 2,000 ms | One collection and persistence cycle |
| UI responsiveness | 100 ms | Synchronous work performed for one user interaction |

Budgets are regression thresholds, not claims about every hardware configuration. Provider latency and explicitly unavailable Windows APIs remain visible rather than being hidden by retries.

## Measured baseline and optimization

The Prompt 38 baseline Release build completed in 3.42 seconds after restore and all 399 tests passed. The existing 100,000-point history responsiveness check passed under its five-second guard.

Code inspection and representative synthetic histories identified `PerformanceAggregationEngine.Downsample` as an allocation and scaling bottleneck: every output bucket used LINQ `Skip`, `Take`, array allocation, and sorting. It now performs a single bounded indexed pass, selects the same maximum-range representative with the same timestamp tie-break, and sorts only the final bounded result. The regression suite validates 250,000 points, peak preservation, ordering, cancellation-independent determinism, and 100 repeated 100,000-point passes with less than 16 MiB retained managed-memory growth.

Production scan execution now emits privacy-safe `ActivitySource` markers and a bounded in-memory observation history. Only a fixed operation name, category, duration, and budget result are recorded; paths, arguments, evidence, and user data are excluded. Budget overruns are warnings and never change diagnostic or repair correctness.

## Soak validation

Run `scripts/Test-PerformanceBudgets.ps1` after a Release build. It repeats the categorized performance suite without retrying failures. CI runs the suite as a blocking quality gate; long-duration Windows hardware soak remains a release-lab activity.

Manual release validation should additionally capture startup, working set, process CPU, handle count, monitoring gaps, scan duration, database P95, report duration, and UI responsiveness on supported Windows 10 x64, Windows 11 x64, and Windows 11 ARM64 systems. A budget miss must be investigated or documented; it must not be hidden by weakening correctness, safety, redaction, or approval behavior.
