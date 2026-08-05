# Packaging and release inputs

WAID supports self-contained x64 and ARM64 installed and portable outputs. Release packaging uses the stable identity and variables in `Package.appxmanifest.template`; only `New-MsixManifest.ps1` renders those values. The publisher must be the exact real certificate subject supplied by the protected release environment. No certificate, password, timestamp credential, signing secret, or fabricated production identity belongs in this repository.

Every publish output contains an unsigned-development label and a SHA-256 artifact manifest. `Build-WaidMsix.ps1` stages validated published files and externally supplied final PNG assets through MakeAppx; `Sign-WaidMsix.ps1` is a separate certificate-subject-checked and signature-verified stage.

Upgrades preserve package identity and SQLite data under `%LOCALAPPDATA%\Windows AI Doctor`. Repair installs preserve data. Downgrades are blocked. Uninstall retains user data by default; data removal requires a separate explicit choice. Portable workspaces coexist without sharing installed data. See `docs/release-packaging.md` for the complete version, location, signing, and VM validation contract.
