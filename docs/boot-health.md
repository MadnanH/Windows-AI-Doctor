# Startup and Boot Analyzer

Boot Health is an offline, read-only inventory and recommendation feature. It collects Windows startup folders, machine and user Run keys, automatic services, boot/logon scheduled tasks, approved shell extensions, WMI startup applications, recent service-start failures, and Diagnostics-Performance boot event 100. WAID does not disable or delete an entry from this page.

## Normalization and evidence

Commands use Windows-aware quoted executable parsing. Equivalent name/command pairs reported by multiple providers are deduplicated, with the more specific provider retained. User profile paths and account names are redacted before storage. Each recommendation includes its source reference, relevant measured value, explanation, action preview, reversibility, and rollback preview.

Critical Windows services, Microsoft Windows tasks, Defender/Windows Security, RPC, and Event Log entries are protected. A protected entry can still display evidence, but WAID offers inspection only and refuses disable simulation.

## Impact calculation

- High: Windows/provider evidence attributes at least 5,000 ms to an entry.
- Medium: measured impact is at least 1,500 ms.
- Low: measured impact is positive but below 1,500 ms.
- Estimated medium: no per-entry measurement is available, median observed boot duration is at least 60 seconds, and at least 25 enabled startup entries exist.
- Unknown: evidence is insufficient. Unknown is never represented as healthy.

Security concern and performance impact are separate. An unknown publisher is a review signal, not a malware verdict. A missing executable can be caused by removable media or packaged-app indirection and is therefore conservative.

## Reversible action design

The action planner currently performs simulation only. A noncritical disable preview records the privacy-safe entry ID, source, source reference, and prior enabled state as rollback metadata. Rollback simulation rejects missing, malformed, or mismatched metadata. A future execution workflow must still require explicit approval, elevation only when the selected source requires it, an audit event, and persisted rollback metadata.

## Limitations

- Per-entry startup duration is not uniformly exposed across supported Windows versions; most entries rely on overall boot correlation.
- Event log retention and Diagnostics-Performance logging policy determine available history.
- Shell extension commands may be CLSIDs rather than file paths.
- Packaged applications, delayed services, and task conditions can make target/enable state appear unavailable.
- Real Windows 10/11, standard-user, and administrator validation remains required.
