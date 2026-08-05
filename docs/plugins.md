# Plugin authoring and security

New plugins target the permissioned [WAID Plugin SDK v2](plugin-sdk-v2.md), implement `IWaidPluginV2`, and register only declared extension points through `IPluginServiceRegistry`. The strict [manifest schema](schemas/waid-plugin-v2.schema.json), [sample](../src/WAID.Plugin.Sample), and [certification checklist](plugin-certification-checklist.md) are canonical.

WAID certifies manifests before loading code: reverse-domain identity, version and host compatibility, API contract, publisher policy, package-path containment, assembly presence and SHA-256, optional Authenticode trust, known capabilities and permissions, permission-to-capability mapping, and plugin dependency versions. The assembly descriptor must exactly match its manifest.

Plugin-private dependencies use collectible load contexts while WAID contracts stay shared. A malformed, incompatible, unauthorized, tampered, or failing plugin is blocked or quarantined with actionable diagnostics and cannot abort startup. Rejected contexts unload immediately. Loaded plugin enable/disable/unload changes require restart because the dependency-injection container can retain service types.

The Plugin Manager supports compatibility preview, explicit permission approval, install-from-file, enable, disable, quarantine, inventory, failure/certification logs, and restart status. State and certification metadata persist in SQLite; management actions are appended to the local audit trail.

API v1 remains load-compatible for previously completed work, but its unrestricted registration contract is never offered to API v2 plugins. Repair extensions remain optional and can only register a repair module; all execution still uses WAID's preview, policy, explicit approval, administrator, backup/restore-point, audit, validation, and rollback safeguards.