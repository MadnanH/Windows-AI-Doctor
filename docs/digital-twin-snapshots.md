# Digital Twin System Snapshots

WAID digital twin snapshots are privacy-safe, point-in-time records intended for change review and repair verification. They are not disk images and cannot restore Windows by themselves.

## Captured scope

Each snapshot records versioned, structured summaries for hardware, operating system, installed drivers, services, startup applications, Windows Update, Windows security, storage, and selected WAID configuration. Components report `Complete`, `Unavailable`, or `Failed`; one unavailable Windows API or provider does not invalidate the rest of the snapshot.

WAID does not inventory personal files or capture file contents, browser data, credentials, authentication material, product keys, or unnecessary device serial numbers. Field names and values are redacted before serialization when they match sensitive categories. Users should still review exported data before sharing it.

## Storage and integrity

The canonical snapshot JSON is protected by a SHA-256 integrity hash and stored as a GZip-compressed SQLite payload. The database keeps only compact indexing metadata outside that payload. Snapshot schema and per-component serializer versions are retained so incompatible data fails explicitly rather than producing a misleading comparison.

Snapshots have a declared purpose: manual, baseline, pre-repair, or post-repair. They may be pinned so retention cannot remove them. Repair snapshots are pinned and linked by repair transaction and related snapshot identifiers. Capture is best-effort and occurs only after the established approval and administrator gates; a snapshot failure never bypasses or weakens repair safety.

## Comparison semantics

Diff strategy `snapshot-diff-v1` compares normalized components and fields in stable ordinal order. It identifies added, removed, and changed values, component availability changes, and elevated-risk changes in security, driver, storage, update, and startup domains. A diff is evidence of change, not proof that a change caused a fault.

## User interface

The Digital Twin page can capture manual or baseline snapshots, pin a snapshot, select two saved snapshots, compare them, inspect component states and risk highlights, and export a redacted JSON representation. Repair-only purposes are intentionally unavailable from manual capture controls.

## Limitations

Several components summarize the latest persisted WAID scan or analysis, so coverage depends on previously completed scanners and local provider availability. Windows editions, permissions, firmware, drivers, and hardware APIs vary. Snapshot completeness and long-duration retention behavior still require validation on the supported Windows 10, Windows 11 x64, and Windows 11 ARM64 hardware matrix.
