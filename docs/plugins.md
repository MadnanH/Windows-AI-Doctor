# Plugin authoring

Plugins are .NET 8 Windows class libraries that reference `WAID.Application` and implement `IWaidPlugin`. Metadata IDs must use reverse-domain notation. `MinimumHostVersion` prevents an incompatible plugin from loading.

`ConfigureServices` may register implementations of `ISystemScanner`, `IRepairModule`, or `IAiAnalyzer`. Repair modules are always invoked through `RepairExecutor`; never expose a direct mutation path that bypasses confirmation, administrator checks, restore points, backups, history, or rollback policy. Keep registration side-effect free; initialize hardware or network resources only when the service is used. Copy the plugin and its private dependencies into the application `Plugins` directory.

The sample in `src/WAID.Plugin.Sample` is the canonical starting point. Production distribution must sign and audit plugin assemblies. Because plugins execute in process with WAID's user permissions, install only trusted plugins. Future marketplace ingestion should verify a manifest hash and Authenticode signature before loading.
