# Packaging groundwork

WAID supports framework-independent x64 and ARM64 publish outputs. Release packaging uses MSIX with the identity in `Package.appxmanifest.template`; the publisher value must be replaced by the real code-signing certificate subject in the protected release environment. No certificate, password, timestamp credential, or signing secret belongs in this repository.

Upgrades preserve the package family identity and the SQLite data under `%LOCALAPPDATA%\Windows AI Doctor`. Normal uninstall follows Windows package behavior and intentionally retains user diagnostic data unless the user explicitly removes it. Signing uses `signtool` or the MSIX Packaging Tool in CI after protected credentials are supplied. Validation evidence must be archived separately; this repository does not claim Windows 10, Windows 11 ARM64, administrator-repair, or hardware certification without a passing JSON report.
