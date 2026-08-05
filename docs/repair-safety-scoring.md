# Repair Safety Scoring and Policy Gates

WAID calculates a transparent deterministic risk score from the current versioned dry run before approval. The score is not a health score or a probability of failure. It is a conservative confirmation and policy input; no score can authorize automatic repair.

## Version and rubric

Scoring version `repair-safety-score-1.0` produces raw factor risk from 0 to 100, applies the fixed weight below, rounds each weighted contribution, and sums the contributions to a 0–100 score.

| Factor | Weight | Risk represented |
|---|---:|---|
| Impact | 25% | Declared safety level and scope of persistent/system effects |
| Reversibility | 15% | Declared backup and rollback support; unsupported rollback is highest risk |
| Privilege | 10% | Administrator-level operating-system access |
| Downtime | 10% | Estimated duration and restart requirement |
| Data risk | 15% | File, registry, and policy targets |
| Uncertainty | 15% | Proportion of effects unknown until execution |
| Validation strength | 10% | Declared rollback and post-execution validation strength |

Each stored factor contains its raw risk, weight, weighted contribution, and plain-language explanation. Identical repair definitions, effects, and policy produce identical results.

## Baseline confirmation

Every repair always requires explicit approval. Score 35 or higher, or a declared Moderate repair, requires a separate risk acknowledgement. Score 70 or higher, or a declared High/Critical repair, requires high-risk acknowledgement. The stronger of score-based and declared-safety requirements wins.

## Policy precedence

Policy version `repair-policy-1.0` may:

- set a maximum allowed score;
- set a maximum declared safety level;
- block repair identifiers;
- require a stronger confirmation type;
- require rollback support for High/Critical or score-70+ repairs.

Policy is applied after baseline scoring. It can block or raise confirmation, but cannot lower a score, lower the baseline confirmation type, declare unsupported rollback, bypass explicit approval, or request elevation during assessment. Equal maximum-score boundaries are allowed; exceeding the boundary by one point blocks execution.

The score and policy are recalculated immediately before approval. A scoring-version, policy-version, score, approval-type, or result change cancels the lifecycle and requires a new simulation. Blocked results never reach preparation or elevation.

## Persistence, audit, and UI

SQLite schema 29 adds normalized score, scoring version, required approval type, and policy result to durable repair orchestration rows. The lifecycle JSON stores every factor, policy requirement, uncertainty exception, versions, explanation, and approval decision. Assessment, rejection, and approval audit events include the score, confirmation type, and policy result without affected paths or evidence values.

The Repair Lifecycle page presents the score, version, required confirmation, policy decision, requirements, exceptions, and the complete weighted factor table before approval controls. The same centralized orchestration gate remains authoritative for queued, recommended, and directly selected repairs.

## Limitations

Weights are conservative engineering policy, not empirical failure probabilities. Runtime Windows state, provider behavior, locked resources, and unknown command effects remain uncertain and increase risk rather than implying safety. The default local policy permits registered repairs up to score 100 and Critical safety while preserving baseline confirmations; managed deployment of organization-specific policy values is not added by Prompt 28. Real high-risk confirmation, assistive-technology presentation, and policy deployment require disposable-VM and enterprise validation.