# Predictive Health Engine

WAID 0.19 introduces a local, transparent statistical foundation for identifying sustained changes in saved health snapshots. It evaluates storage wear, crash rate, thermal trend, memory instability, update failures, and performance decline.

## Safety and interpretation

A prediction is an early monitoring signal, not a hardware-failure guarantee or a diagnosis. The engine requires at least five observations spanning three days. It suppresses weak, noisy, contradictory, or drifting signals and never initiates a repair. Users should verify cited source evidence before maintenance or replacement decisions.

Each result records the model version, feature version, prediction horizon, bounded risk range, confidence interval, validation outcome, source references, explanation, and monitoring recommendation. The current `transparent-linear-v1` model uses ordinary least-squares slope, residual error, directional consistency, drift guards, and one-point holdout backtesting. The `IPredictiveHealthModel` contract permits a future model implementation without changing persisted report semantics.

## Privacy

Learning and analysis are local by default. The engine consumes WAID health snapshots and stores derived numeric features and WAID source references in SQLite. It does not upload data, inspect personal file contents, collect browser activity, or execute cloud inference.

## Limitations

- Device firmware and Windows providers may omit or report incompatible health fields.
- Short histories, irregular workloads, hardware changes, and missing observations reduce reliability.
- Confidence ranges express model uncertainty, not probability of failure.
- Validation is lightweight historical backtesting, not clinical or manufacturer certification.
- Real hardware and long-duration calibration remain required before predictive claims can be certified.
