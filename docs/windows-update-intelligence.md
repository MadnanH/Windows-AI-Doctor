# Windows Update Intelligence

Update Health is an offline-first, read-only diagnosis of local Windows Update and servicing evidence. It collects up to 100 update-history records, required service state, Windows Update machine policy, supported reboot markers, DISM `CheckHealth` summary, recent servicing events, and system-drive free space. It does not search for, download, install, hide, or remove updates.

## Cause classification

The offline error catalog and correlated system state keep these causes separate:

- Network: endpoint, proxy, DNS, or timeout codes such as `0x8024402C` and `0x80072EE2`.
- Servicing: component-store/source errors such as `0x800F081F`, `0x80073712`, DISM, and CBS signals.
- Policy: configured update access blocks such as `0x8024002E` or `NoAutoUpdate`.
- Storage: `0x80070070` or less than 10 GiB available on the Windows system drive.
- Reboot: Component Based Servicing, Windows Update, or pending-file-rename markers.
- Service: stopped Windows Update, BITS, or Cryptographic Services dependencies.

Unknown codes remain unknown. Missing history, unavailable DISM output, or absent event logs are limitations rather than proof of health. Each diagnosis includes confidence, severity, timestamped evidence, and a source reference.

## Supported plan and escalation

The plan orders prerequisites conservatively: pending restart, supported storage cleanup, service restoration, network validation, DISM/SFC through WAID's existing safe repair workflow, then policy-administrator escalation. Every step is a simulation until explicitly approved. Privileged steps declare administrator requirements; restart requirements are shown before approval. Policy recommendations never bypass managed settings.

No plan step removes an update. If update removal is ever added, it requires a separate future design with explicit per-update approval, applicability validation, recovery planning, audit history, and rollback limitations.

## Limitations

- The Windows Update COM history and event retention window vary by Windows version and cleanup history.
- DISM `CheckHealth` may require elevation; unavailable results remain explicitly degraded.
- The bundled catalog explains common codes offline and does not claim exhaustive coverage.
- WSUS, Windows Update for Business, and MDM policy interpretation may require the organization's administrator.
- Real Windows 10/11, managed-device, offline, standard-user, and administrator validation remains required.
