# Plugin authoring

Plugins are .NET 8 Windows class libraries that reference `WAID.Application` and implement `IWaidPlugin`. Metadata IDs must use reverse-domain notation. `MinimumHostVersion` prevents an incompatible plugin from loading.

`ConfigureServices` may register implementations of `ISystemScanner`, `IRepairModule`, or `IAiAnalyzer`. Repair modules are always invoked through `RepairExecutor`; never expose a direct mutation path that bypasses confirmation, administrator checks, restore points, backups, history, or rollback policy. Keep registration side-effect free; initialize hardware or network resources only when the service is used. Copy the plugin and its private dependencies into the application `Plugins` directory.

The sample in `src/WAID.Plugin.Sample` is the canonical starting point. Production distribution must sign and audit plugin assemblies. Because plugins execute in process with WAID's user permissions, install only trusted plugins. Future marketplace ingestion should verify a manifest hash and Authenticode signature before loading.
# Plugin security model

Each plugin requires a sidecar `*.waid-plugin.json` manifest containing its stable id, display name, semantic version, publisher, minimum WAID host version, entry assembly, API version, and declared capabilities. WAID accepts API version 1, checks host compatibility, constrains entry assemblies to the plugin directory, and requires an allow-listed publisher. Organizations can additionally require a trusted Windows Authenticode chain.

Plugin-private dependencies load through a collectible `AssemblyLoadContext`; WAID contracts remain shared with the host. Invalid, incompatible, untrusted, or crashing plugins are represented as quarantined diagnostics and never abort application startup. Disabled plugin IDs persist atomically in `plugin-state.json`. Rejected contexts unload immediately; loaded plugins require restart to unload safely because the dependency-injection container can retain their service types.
