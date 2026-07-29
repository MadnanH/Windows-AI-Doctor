# Reliability Timeline

WAID 0.21 builds a local, versioned chronological projection from existing immutable diagnostic records. Rebuilding the timeline does not edit scans, repairs, driver reports, update reports, health snapshots, or monitoring samples.

## Sources

- Scanner findings, including crash/BSOD and Reliability Monitor evidence
- Windows Update attempts
- Driver inventory changes
- Repair transactions and outcomes
- Significant overall-health changes of at least ten points
- Warning and critical live-monitoring samples
- Monitoring retention state, represented explicitly when earlier records were removed

Every projected event shows its source type, source identifier, evidence reference, UTC occurrence time, category, severity, subject, and projection version. Stored ordering uses UTC descending and stable event IDs; the UI explains that displayed times are local.

## Deduplication and grouping

`timeline-v1` derives a stable ID from source type, source ID, event kind, and normalized UTC instant. Identical source events deduplicate without changing the source record.

`temporal-subject-v1` groups events within 30 minutes when they share a normalized subject or belong to a narrowly declared cross-category relationship, such as a driver change followed by a crash. Every group is labeled as a possible association and explicitly states that it is not proof of causation. Grouping is deterministic and can be rebuilt when a future version changes strategy.

## Queries, gaps, comparison, and export

The SQLite projection supports stable pages of up to 200 events, category/date/search filtering, incident lookup, and indexed UTC/category ordering. The UI provides timeline and table modes, source evidence, incident membership, two-event elapsed-time comparison, and filtered JSON export. Retention gaps remain visible rather than implying continuous history.

Exports remove secret-like fields and redact the current user profile path. They contain projected evidence only and do not include personal file contents, browser data, credentials, or product keys.

## Limitations

Timeline groups are deterministic correlation candidates, not causal conclusions. Source availability and retention determine coverage. Health shifts use the overall score and a fixed ten-point threshold. The projection is rebuilt on demand in this milestone; it does not continuously watch source tables. Large-history tests cover 10,000 records, but long-duration UI and source-provider validation still require supported Windows hardware.
