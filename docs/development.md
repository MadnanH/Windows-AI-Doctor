# Development and operations

## Quality gate

Run `.\build.ps1` before every change is merged. The repository treats compiler warnings as errors and produces deterministic builds. `publish.ps1` creates a self-contained unpackaged Windows build under `artifacts/publish`.

## Adding a scanner

Implement `ISystemScanner`, use a globally unique ID, never mutate the system, honor cancellation, and return evidence that is safe to persist. Register the scanner as an `ISystemScanner` in dependency injection.

## Adding a repair

Implement `IRepairAction`. Validate the finding, make the smallest reversible change, report whether restart is required, and never embed untrusted values in PowerShell source. Administrative repairs must set `RequiresAdministrator`.

## Logs and database

Rolling logs and `waid.db` live in `%LOCALAPPDATA%\Windows AI Doctor`. Logs are retained for 14 days. Do not record secrets, command credentials, or personal file contents.

## Release checklist

1. Run the Release build and tests for x64 and ARM64.
2. Exercise scan cancellation, a standard-user run, and an elevated repair run.
3. Validate schema upgrade behavior against the previous released database.
4. Sign application and plugin binaries using the organization code-signing certificate.
5. Publish SBOM, privacy statement, and release notes with the installer.
