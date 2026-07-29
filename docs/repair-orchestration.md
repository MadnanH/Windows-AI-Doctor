# Repair Orchestration Framework

WAID routes repair execution through one durable, typed lifecycle: Requested, Assessing, Assessed, Simulated, AwaitingApproval, Approved, Preparing, Executing, Validating, Committing or RollingBack, and a terminal outcome. Invalid transitions fail closed.

## Safety guarantees

Assessment and simulation are read-only. They validate the registered repair definition, policy, affected resources, dependencies, and conflicts before presenting the plan. Execution requires a persisted current plan and explicit user approval. High-risk repairs additionally require a separate risk acknowledgement. Administrator checks, restore-point creation, backup, execution, and rollback remain centralized in `RepairExecutor`; neither the queue nor UI can bypass them.

Only one orchestration may execute at a time. Execution has a bounded configurable timeout and accepts cancellation. Cancellation during preparation or execution is checkpointed and invokes the existing rollback policy when a usable backup and rollback support are available. WAID never retries or re-runs a repair automatically.

## Validation and recovery

After a successful typed execution result, the configured validator records versioned validation evidence. A passed validation commits the lifecycle outcome. A failed validation enters `RecoveryRequired` so an operator can inspect repair history and backup state; it is never silently treated as success.

Lifecycle state is saved after every significant transition in SQLite schema 26. On startup, plans interrupted before execution are cancelled without mutation. Plans interrupted during or after execution enter `RecoveryRequired`. WAID does not infer whether an interrupted operating-system command completed and does not automatically repeat it.

## Audit and privacy

Assessment requests, approval decisions, rejection, success, cancellation, rollback, validation failure, timeout, and recovery decisions are appended to the audit trail. Stored and displayed affected paths replace the Windows user-profile prefix with `%USERPROFILE%`. Logs contain identifiers, stages, and typed outcomes rather than command output or secret values.

## User interface

Repair Lifecycle is reachable from the main navigation. It presents the step-by-step plan, affected actions, prerequisites, conflicts, safety requirements, rollback support, approval controls, live progress, restart state, validation evidence, outcome, and recent durable lifecycle records. Closing or restarting the application cannot convert an interrupted repair into an automatic retry.

## Limitations

The default validator confirms the typed executor outcome and explicitly labels restart-required validation as provisional. Repair-specific postcondition validators may add stronger checks in later work. A process or machine failure can prevent in-process rollback; WAID preserves the last durable checkpoint and backup reference and requires operator review. Restore-point availability and actual Windows rollback behavior vary by Windows configuration and still require disposable-VM validation. No repair is claimed safe for production hardware until that validation evidence is archived.