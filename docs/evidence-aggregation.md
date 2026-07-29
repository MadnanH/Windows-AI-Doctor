# Evidence Aggregation and Correlation Semantics

WAID evidence graph schema `1.0` normalizes recent scanner findings and repair outcomes into immutable nodes. Domains are Event, Driver, Update, Storage, Network, Crash, Performance, Repair, and Other. Each node retains first/last observation time, freshness, normalized attributes, duplicate count, and every contributing provenance record.

## Relationships

Relationships are derived observations, never causal conclusions:

- `TemporalAssociation`: same-domain signals occurred inside the configured window.
- `SharedSignal`: normalized codes agree across observations.
- `ConflictingObservation`: sources report different values for a common normalized attribute.
- `RepairFollowUp`: a repair and observation occurred inside the follow-up window.
- `CausalCandidate`: cross-domain timing may justify further investigation.

Every relationship stores confidence, rationale, strategy ID/version, endpoints, and `IsCausalClaim=false`. Direction is deterministic and cycle prevention rejects a relationship that would create a path back to its source.

## Deduplication, freshness, and retention

Exact normalized domain/code/summary/minute observations are deduplicated while all provenance is retained. Defaults use a six-hour correlation window, current age of 24 hours, recent age of seven days, and retention of 90 days. Historical nodes remain visible until retention expiry. The engine caps a run at 10,000 nodes and uses a time-ordered sliding window so unrelated old observations do not create quadratic comparison work.

## Persistence and privacy

SQLite schema 17 stores graph runs, node JSON, relationship JSON, versions, confidence, timestamps, and retention deadlines. Saving is transactional and expired runs are deleted through foreign-key cascading. Inputs come from WAID's already-sanitized findings and repair history; the graph does not read browser data, credentials, personal files, or document contents. Raw input objects are copied and never modified.

The Incident & Evidence Explorer offers accessible list and relationship modes plus domain filters. UI wording consistently labels edges as associations rather than causation.