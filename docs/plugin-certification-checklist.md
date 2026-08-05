# Plugin certification checklist

Before distributing a WAID plugin:

- [ ] Build against .NET 8 and the published WAID Application contracts.
- [ ] Use a stable reverse-domain plugin ID and API version 2.
- [ ] Match assembly metadata, capabilities, permissions, and dependencies to the manifest exactly.
- [ ] Request only the minimum permissions required for each extension point.
- [ ] Do not access WAID SQLite files, secrets, browser data, personal files, or repair executors directly.
- [ ] Keep every dependency within the package and declare WAID plugin dependencies with minimum versions.
- [ ] Include the entry assembly SHA-256 in the release manifest.
- [ ] Authenticode-sign the assembly when the target organization requires signatures.
- [ ] Pass malformed-input, cancellation, failure-isolation, compatibility, permission, and unload tests.
- [ ] Prove scanners return real observations and explicit unavailable states—never simulated production results.
- [ ] Prove report/evidence output follows WAID redaction rules.
- [ ] Prove repair modules only declare plans and cannot bypass explicit approval or safety controls.
- [ ] Install through Plugin Manager and review all certification results and requested permissions.
- [ ] Verify enable, disable, quarantine, restart, audit, and failure-log behavior on Windows 10 and Windows 11.