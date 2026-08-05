# Portable Edition

Publish with .\publish.ps1 -Platform x64 -Portable. The output contains waid.portable and PORTABLE-README.txt.

On first use, choose a workspace with WAID.Desktop.exe --portable --workspace X:\WAID-Workspace. With a portable marker and no argument, the adjacent WAID-Workspace folder is used. The title and navigation footer show portable mode and the active workspace.

All writable configuration, SQLite data, logs, audit, reports, backups, crash logs, profiles, knowledge indexes, and state use the workspace. Portable options do not reference machine-wide configuration. Schema migrations and pre-migration backups remain inside the workspace. Plugins are read from beside the executable.

A process-local guard and cross-process mutex prevent concurrent access to one normalized workspace; different workspaces and installed WAID can coexist. Startup performs a flushed write probe. Read-only, missing, disconnected, or denied media produces an actionable recovery screen before scanners or repairs start.

Close WAID before removing media. Clean removal deletes the executable output and chosen workspace. Programmatic cleanup requires explicit approval plus the WAID marker and cannot target an arbitrary folder. Export or back up needed history first.

Portable mode never bypasses elevation or repair safeguards. Simulation, explicit approval, policy, backup, audit, validation, and rollback remain authoritative.
