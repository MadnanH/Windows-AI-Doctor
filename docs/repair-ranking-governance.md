# Repair Recommendation Ranking and Governance

WAID ranking version `deterministic-v2` is recommendation-only. It cannot execute, approve, register, or modify a repair. Candidate IDs originate exclusively from diagnosis mappings that resolve through the active `RepairRegistry`; unknown IDs are omitted.

## Eligibility gates

Before ranking, each candidate is evaluated against active blocked IDs, maximum permitted safety level, declared conflicts, and the prerequisites derived from its registered repair policy: administrator capability, restore-point capability, and backup capability. Decisions are stored as Eligible, BlockedByPolicy, Conflict, or PrerequisiteMissing. Only Eligible candidates enter the ranked plan.

High-risk actions always have `AutoSelectable=false`. Low risk is the only category that can be marked eligible for grouped review, and execution still creates an individual approval and audit record and passes through every normal safety gate.

## Deterministic factors

The score is clamped to 0-100 and combines:

- evidence strength from supporting findings and correlations;
- estimated benefit from confidence, evidence, and severity;
- safety-level risk penalty;
- rollback/reversibility bonus;
- estimated downtime penalty and configured downtime context;
- conflict and missing-prerequisite penalties;
- policy rejection penalty;
- bounded outcome-feedback adjustment.

Dependency ordering remains deterministic: DISM before SFC, followed by network-stack steps in safe dependency order. Ties use expected benefit and stable repair ID ordering.

## Feedback governance

Feedback aggregates store only bounded counts of successes, failures, and rollbacks. At least three outcomes are required before feedback affects ranking. The adjustment is limited to -10 through +10 and cannot override eligibility, policy, conflict, prerequisite, registration, approval, or execution safeguards. Outcome data does not create new repair mappings.

## Persistence and UI

SQLite schema 18 stores complete ranking runs as versioned JSON, including factors, accepted/rejected decisions, conflicts, missing prerequisites, auto-selection eligibility, and bounded feedback aggregates. The Repair Plan comparison displays benefit, risk, ranking version, downtime, prerequisites, factors, simulation actions, administrator requirement, restart, backup, restore point, and rollback support. WAID does not present a one-click fix-all operation.