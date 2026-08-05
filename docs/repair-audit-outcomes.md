# Repair Audit Retention and Outcome Interpretation

WAID records each persisted repair lifecycle transition in a separate append-only SQLite audit chain. Entries identify the orchestration and transaction, repair, UTC time, actor, stage category, redacted summary, validation evidence, and outcome classification. Duplicate identities are ignored; existing entries are never updated or deleted by normal application operations.

Outcome aggregates are disposable derived data. Refreshing Repair History rebuilds them deterministically from immutable terminal audit entries, one final observation per orchestration. A successful observation requires an explicit successful validation record. Failed validation, rollback, failure, and cancellation remain separate outcomes.

Statistics are descriptive only. They cannot register a repair, change knowledge, alter safety scoring or policy, bypass approval, schedule execution, or generate commands. Small samples and differences between machines limit interpretation.

Audit chain data follows the commercial repair-history retention policy and is retained independently from rebuildable aggregates. Database backup/restore includes both. The redacted JSON export excludes known secret assignments and replaces the current user-profile path. Users should still review exports before sharing.

The History page filters by exact repair ID and outcome, shows before/after evidence declared by the simulation and validator, approval actor, transaction/rollback details, outcome statistics, and redacted export. Storage or export failures are actionable and do not silently change repair results.
