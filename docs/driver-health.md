# Driver Conflict Analyzer

WAID's Driver Health feature is read-only. It inventories Plug and Play devices and signed-driver metadata through Windows CIM, collects a bounded 30-day set of relevant System events, compares the result with the previous WAID snapshot, and stores the normalized report in SQLite. It never downloads, installs, removes, enables, disables, or rolls back a driver.

## Signals

- Device Manager problem codes, with disabled code 22 distinguished from failed-start/load codes 10, 31, 39, and 43.
- Windows-reported signed-driver state for non-Microsoft packages.
- Present devices sharing one hashed hardware identity. This is informational because docks and virtual devices can legitimately create duplicate records.
- Old, non-Microsoft, non-present packages. This is informational and does not recommend deletion.
- Architecture mismatches when both Windows and driver architecture are available.
- Version changes between consecutive WAID snapshots.
- Device-linked Kernel-PnP/driver-load events 219 and 7026, display recovery event 4101, and driver install/update events 20001 and 20003.

Every finding contains a confidence value, source references, observation time, plain-language explanation, and conservative next action. Raw device and hardware identifiers are SHA-256-derived privacy-safe keys; user names and long event text are sanitized before persistence.

## Failure and privilege behavior

Collection does not request elevation. Standard-user reports explicitly state that protected details may be unavailable and lower signature confidence accordingly. Unsupported CIM or event providers return a typed, actionable collection failure; the UI does not infer missing information as healthy. Cancellation is passed through the provider, analyzer, and repository.

## Limitations

- `Win32_PnPSignedDriver.IsSigned` describes Windows catalog/signing state; it is not a malware verdict and does not replace vendor verification.
- Driver dates are package metadata, not proof of install time, so WAID uses snapshot differences and matched installation events for recent-change findings.
- Event-to-device correlation requires the sanitized Windows message to contain the device display name; unmatched events are not attributed.
- Duplicate and orphan signals are deliberately informational because disconnected, virtual, and docked hardware creates legitimate duplicates.
- Windows driver APIs vary by release, architecture, device class, and permissions. Physical Windows 10/11 validation remains required.
