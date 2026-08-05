# WAID Plugin SDK v2

WAID plugins are offline, permissioned extensions loaded only after local certification. API v2 is the stable contract for WAID 0.34. A plugin cannot access WAID's service provider or SQLite connection through the SDK; it receives only `IPluginServiceRegistry`.

## Package

Place one `.waid-plugin.json` manifest and its entry assembly in the same package directory. The entry path must stay within that directory. Install through **Plugin Manager > Install from file**. WAID shows every compatibility, signature, capability, permission, dependency, and integrity check before installation. Permissions require explicit user approval.

Required manifest fields are defined by [the JSON schema](schemas/waid-plugin-v2.schema.json). Use reverse-domain IDs, semantic `System.Version` values, API version `2`, a publisher approved by host policy, and an optional uppercase/lowercase SHA-256 digest. Dependencies identify other WAID plugins and a minimum version.

## Extension points

- `Scanner`: register `ISystemScanner`; use `SystemRead`, `EnvironmentRead`, `EventLogRead`, or `NetworkProbe` as appropriate.
- `ReportContributor`: register `IPluginReportContributor`; requires `ReportWrite`.
- `KnowledgeProvider`: register `IPluginKnowledgeProvider`; requires `KnowledgeRead`.
- `RepairModule`: optional and requires `RepairPlan`. Repairs still pass through WAID policy, preview, approval, backup/restore-point, execution, audit, and rollback controls. Plugins cannot silently run repairs.

Implement `IWaidPluginV2`. Its `Sdk` descriptor must exactly match the certified manifest, and `Configure(IPluginServiceRegistry)` may register only declared extension points. The legacy `ConfigureServices` method is never invoked for API v2 plugins.

## Lifecycle and isolation

Certification occurs before assembly loading. Compatible enabled plugins load in collectible dependency contexts; WAID contracts are shared with the host. Validation/load/registration failures are quarantined and cannot abort host startup. Enable, disable, install, and active-plugin unload changes require restart because registered singleton services may retain plugin types. Rejected contexts are unloaded immediately. Inventory, certification outcome, signature state, permissions, failures, state changes, and user actions are stored/audited locally.

## Compatibility

API v1 remains load-compatible for existing plugins. New development must target API v2. A plugin is blocked with actionable diagnostics when its API, host version, publisher, permissions, dependency versions, manifest, hash, or signature policy is incompatible.

## Sample

`src/WAID.Plugin.Sample` demonstrates a real environment scanner, API v2 descriptor, controlled registration, and matching manifest. Run the full solution tests before distribution and complete the [certification checklist](plugin-certification-checklist.md).