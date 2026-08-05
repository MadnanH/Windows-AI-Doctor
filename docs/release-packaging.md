# Installer, packaging, signing, and update readiness

WAID produces self-contained installed and portable layouts for Windows x64 and ARM64. `publish.ps1` is the only publish entry point. It labels every development output as unsigned and writes `waid-artifact.json` with version, channel, architecture, edition, lengths, and SHA-256 hashes for every shipped file.

## Release sequence

1. Build and test the exact commit with `build.ps1 -Configuration Release`.
2. Publish each architecture and edition with `publish.ps1 -Platform x64|ARM64 [-Portable] -Channel dev|beta|stable`.
3. Validate each output with `scripts/Test-PublishedArtifact.ps1`. ARM64 outputs must be launch-tested on ARM64 Windows; cross-architecture CI performs integrity validation only.
4. Supply final PNG identity assets and the exact certificate subject from the protected release environment. Render `AppxManifest.xml` through `scripts/New-MsixManifest.ps1`.
5. Build the unsigned MSIX with `scripts/Build-WaidMsix.ps1` and the Windows SDK `MakeAppx.exe`.
6. Sign only the immutable candidate with `scripts/Sign-WaidMsix.ps1`. The certificate must be outside the repository, currently valid, and have a subject exactly matching the manifest publisher. The script verifies the resulting signature with `signtool /pa /all`.
7. Archive artifact manifests, hashes, signatures, clean-install/upgrade/uninstall reports, and the commit hash. Never rebuild after approval or signing.

No certificate, private key, password, timestamp credential, or fabricated production publisher is stored in source control. Unsigned artifacts are development outputs and are not production-installable claims.

## Identity, version, and channels

The stable MSIX identity is `WindowsAIDoctor`. Keeping that identity, publisher, and architecture stable allows Windows package upgrade semantics. SemVer prerelease/build labels are removed when converted to the required four-part numeric MSIX version. A candidate lower than the installed numeric version is blocked. An equal version is a repair install; a greater version is an upgrade.

The application footer shows the informational version and channel. Prerelease builds show `Unsigned development channel`, and their window title includes `Development`. Stable signed builds show the stable channel.

## Data and uninstall behavior

Installed application binaries belong to the Windows package. Installed mutable data remains under `%LOCALAPPDATA%\Windows AI Doctor`; portable data remains in its selected marked workspace. They do not share a database, configuration, logs, plugins, backups, or locks.

Upgrades and repair installs replace application files but preserve the installed data directory. Database migrations remain transactional, create an upgrade backup, and reject newer schemas without modifying them. Uninstall removes application files. The default data choice is **retain** so diagnostic history is not silently destroyed. Removing local data is a separate explicit choice and must show the exact data root before approval. MSIX itself does not silently delete external local data.

## Required VM matrix

Before production release, use disposable snapshots to validate clean install, first launch, standard-user launch, administrator repair safeguards, same-identity upgrade with data preservation, equal-version repair install, downgrade block, uninstall with retained data, explicitly approved data removal, installed/portable coexistence, offline launch, signature trust, Windows 10 x64, Windows 11 x64, and Windows 11 ARM64. Archive evidence; passing CI is not a substitute for these tests.

Production MSIX creation and signature verification require final identity assets, Windows SDK tools, protected signing credentials, and the VM matrix. This repository provides and tests the release path but does not claim those external steps have occurred.
