# Configuration, Settings, Profiles, and Feature Flags

WAID resolves configuration into an immutable snapshot at the start of an operation. Later changes do not alter that snapshot. Precedence is deterministic, from lowest to highest:

1. Built-in safe defaults.
2. Machine configuration.
3. Local user settings.
4. Active imported profile.
5. Current-session overrides.
6. Administrator policy.

Policy is always applied last. A policy can list locked setting keys; the UI disables those controls and user, profile, and session values cannot bypass the enforced value. Invalid files, unknown flags, unsupported versions, blank sources, or locks without an enforced value fail closed and do not produce an operation snapshot.

## Settings catalog

| Setting | Scope support | Default | Privacy and behavior |
| --- | --- | --- | --- |
| `RunScansAtStartup` | All | `false` | Allows local scanning after WAID starts; it does not create a background service. |
| `EnableAiAnalysis` | All | `false` | Enables deterministic offline analysis. No diagnostic data is uploaded. |
| `AllowTelemetry` | All | `false` | Records user permission only. No telemetry transport is implemented. |
| `AiProvider` | All | `None` | Selects a provider identifier. Current production analysis remains offline. |
| `Theme` | All | `System` | UI preference; valid values are `System`, `Light`, and `Dark`. |
| `ScanTimeoutSeconds` | All | `120` | Valid range 10–3600 seconds; scanners retain their own bounded policies. |
| `EnableExperimentalFeatures` | All | `false` | Master safety gate. Experimental flags are forced off when this is false. |

“All” means machine, user, profile, session, and policy layers may provide a value. Machine and policy configuration are read-only to WAID and require no elevation merely to read. Deployment tooling is responsible for administrator-only ACLs on `%PROGRAMDATA%\Windows AI Doctor\machine-settings.json` and `policy-settings.json`.

## Feature flags

| Flag | Default | Experimental | Effect |
| --- | --- | --- | --- |
| `advanced-event-correlation` | Off | No | Enables consumers to opt into additional local correlation rules. |
| `experimental-repair-planning` | Off | Yes | Gates future experimental recommendation ordering. It never authorizes a repair. |
| `cloud-ai-provider` | Off | Yes | Reserved configuration gate; no cloud provider or network implementation exists. |

Unknown flags are rejected. Experimental flags require the effective `EnableExperimentalFeatures` value to be true; otherwise the immutable snapshot reports them disabled from `SafetyDefault`. Profile import also requires an explicit experimental acknowledgement when the profile requests the master gate or an experimental flag.

## Machine and policy files

Files are JSON, limited to one megabyte, reject unknown JSON members, and use document version 1. The layer scope must match the file. Example policy:

```json
{
  "version": 1,
  "layer": {
    "scope": "Policy",
    "source": "Contoso workstation policy",
    "values": {
      "allowTelemetry": false,
      "enableExperimentalFeatures": false,
      "scanTimeoutSeconds": 300
    },
    "flags": {
      "experimental-repair-planning": false
    },
    "lockedSettings": [
      "AllowTelemetry",
      "EnableExperimentalFeatures",
      "ScanTimeoutSeconds"
    ]
  }
}
```

Enum values use their names. Policy locks are allowed only in a policy layer and must name a setting for which the same layer supplies a value.

## Profiles, reset, and persistence

The Settings page is categorized and searchable. A privacy-safe `.waid-profile.json` export contains only the user's settings and feature choices. It excludes logs, evidence, repair history, system identifiers, secrets, tokens, product keys, and personal files. Imports validate the document before changing state and preserve administrator policy precedence.

Reset removes local user values, the active profile, and session overrides. It cannot remove machine configuration or policy. User saves, session changes, profile import/export, and reset are audit events.

SQLite schema 8 stores configuration state version 2, including source, user values, feature choices, active profile, and update time. Migration 8 copies the legacy `settings` JSON into a version-1 state. The repository validates and deterministically upgrades it to version 2 on first read while preserving values.
