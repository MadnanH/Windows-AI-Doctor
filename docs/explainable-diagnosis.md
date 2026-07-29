# Explainable Diagnosis Contract

WAID diagnosis explanations are deterministic, offline, immutable records. Schema version `1.0` contains the problem statement, rationale, supporting evidence, alternatives, counter-evidence, impact, urgency, next step, historical change, confidence calibration, and the rule/calibration versions that produced the result.

## Confidence interpretation

| Score | Band | Meaning |
|---:|---|---|
| 90-99 | Very high | Multiple strong signals align; verification is still required before repair. |
| 75-89 | High | Evidence strongly supports the cause, with remaining uncertainty. |
| 50-74 | Moderate | Evidence is mixed or incomplete; collect more evidence. |
| 0-49 | Low | Evidence is insufficient for a reliable machine-specific conclusion. |

Scores are capped at 99 because local evidence cannot prove a diagnosis with absolute certainty. Required rule evidence has weight `1.0`, optional matching evidence `0.65`, and correlated evidence adds `0.25`. A contradictory signal (`NO_<CODE>` or `<CODE>_HEALTHY`) subtracts 12 confidence points and is retained as counter-evidence. Identical normalized findings and rule versions produce identical confidence.

Confidence is guidance, not probability of repair success. Repair safety, approval, administrator requirements, backups, restore points, and rollback remain independent gates.

## Persistence and compatibility

Explanations are serialized within the existing SQLite `diagnosis_reports.report_json` document, so schema 16 remains unchanged. Each explanation persists evidence identifiers and source references, alternatives, calibration metadata, explanation schema version, and knowledge-rule version. Reports created before schema 1.0 deserialize through the explicit unsupported legacy explanation and do not fabricate evidence.

## Rendering

The diagnosis page, dashboard, history timeline, recommended-repair preview, grounded chat retrieval, HTML export, and PDF export consume the same persisted contract. The invariant plain-text renderer provides the standard section order for chat and snapshot tests. Unsupported findings receive an explicit unsupported explanation and never receive a repair recommendation merely to fill the UI.

## Limitations

The engine explains only rules and evidence available in the bundled offline knowledge base. Counter-evidence naming is a versioned rule convention, not free-text inference. Historical comparison uses the latest persisted report with the same rule ID. Explanations must be reviewed alongside the cited evidence and never authorize a repair.