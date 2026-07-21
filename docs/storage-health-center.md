# Storage Health Center

Storage Health Center unifies read-only Windows Storage Management, volume, reliability-counter, and System event evidence. Physical disks and volumes remain separate models. Raw unique IDs are SHA-256-derived privacy-safe keys; user names and profile paths are redacted before persistence.

## Data sources and thresholds

- `Get-PhysicalDisk` supplies device type, bus, size, health, and operational state.
- `Get-StorageReliabilityCounter` may supply temperature, wear, and maximum read/write latency. Vendor support and units vary, so unavailable values remain unknown and warnings use conservative confidence.
- `Get-Volume` supplies filesystem, capacity, free space, and volume health.
- System events 7, 11, 51, 55, 98, 129, and 153 provide recent disk/filesystem evidence.
- Capacity warns below 10% free and becomes critical below 5%; temperature warns at 60°C and becomes critical at 70°C; reported wear warns at 80%; provider maximum latency warns at 100 ms. Every warning cites its source and uncertainty.

Snapshots persist trends such as free-byte change without storing device serial numbers. Metrics are stored in bytes, milliseconds, Celsius, and percent; UI formatting does not change stored units.

## Cleanup and folder analysis

Cleanup preview estimates only bounded temporary-file and Windows Error Reporting archive categories. Every category is marked as requiring explicit selection. Estimates do not delete, move, open, or modify files.

Large-folder analysis accepts only an absolute path, walks folders iteratively, supports cancellation, records inaccessible redacted paths, and returns partial totals when cancelled. It never reads file contents or deletes files.

## Limitations

- USB bridges, RAID controllers, Storage Spaces, virtual disks, and vendor firmware can omit or reinterpret SMART/reliability data.
- A single latency maximum or filesystem event does not prove hardware failure; corroborate repeated evidence and vendor diagnostics.
- Cleanup estimates are bounded snapshots and can change before a future approved action.
- Real Windows 10/11, HDD, SATA SSD, NVMe, Storage Spaces, removable-drive, standard-user, and administrator validation remains required.
