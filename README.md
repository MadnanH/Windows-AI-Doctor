# Windows AI Doctor

Windows AI Doctor (WAID) is a commercial-grade Windows diagnostics and repair foundation built with C# 12, .NET 8, WinUI 3, Clean Architecture, and MVVM.

## Capabilities

- Extensible, cancellation-aware system scanner and repair pipelines
- Local AI abstraction with a deterministic offline analyzer
- SQLite scan history and settings storage
- Safe parameterized PowerShell execution with structured results
- Transactional repair execution with confirmation, restore points, backups, rollback, and history
- Versioned plugin contract and isolated plugin directory
- Serilog rolling-file diagnostics
- WinUI 3 dashboard and settings experience
- xUnit coverage for domain invariants and orchestration

## Build

Prerequisites: Windows 10 version 1809 or newer, Visual Studio 2022 17.8+ with the .NET desktop and Windows App SDK workloads, and .NET SDK 8.0.419 or a compatible patch.

```powershell
.\build.ps1
```

Open `WindowsAIDoctor.sln` in Visual Studio, select `WAID.Desktop`, `x64`, and run. The unpackaged application stores data under `%LOCALAPPDATA%\Windows AI Doctor`.

See [Development progress](WAID_PROGRESS.md), [Architecture](docs/architecture.md), [Development](docs/development.md), and [Plugin authoring](docs/plugins.md).
