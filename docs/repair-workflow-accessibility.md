# Repair Workflow and Accessibility

## Safe workflow

The Repair Lifecycle page presents one interruption-safe workflow: plan review, evidence and dry-run effects, safety policy and approval, execution progress, restart guidance, validation, rollback/recovery, and reports. Simulation remains read-only. Execution requires the current persisted plan, explicit approval, and every backend safety acknowledgement.

An application restart preserves only a simulated plan waiting for approval. The page restores that plan and requires fresh explicit approval. Unsafe interrupted records become RecoveryRequired; WAID never retries them automatically.

## Accessibility contract

- Primary controls and status surfaces have stable automation identifiers and accessible names.
- Native controls preserve keyboard operation and focus behavior.
- Live regions announce the exact current action and status.
- Text wraps, pages scroll, and fixed page heights are avoided.
- Theme resources support Windows high contrast.
- Users can switch between plain-language and technical summaries.
- Unavailable and recovery-required states are stated without relying on color.

Run scripts/Test-AccessibilityNavigation.ps1 for XAML and identifier validation. Keyboard-only navigation, Narrator, high contrast, and 200% text scaling still require manual Windows validation.

## Cancellation and recovery

Cancellation is cooperative. Users must inspect persisted lifecycle state and recovery artifacts before retrying. Rollback is separate and explicitly confirmed. UI summaries are presentation-only; backend lifecycle, approval, safety policy, administrator checks, and artifact validation remain authoritative.
