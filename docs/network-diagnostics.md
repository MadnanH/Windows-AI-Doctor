# Network Diagnostic Center

The Network Health page inspects Windows adapter and IP configuration, default routes and gateways, configured DNS servers, user proxy state, connected VPN state, Wi-Fi adapter metadata, firewall profiles, and the DHCP, DNS Client, and Network Location Awareness services.

Local collection is read-only. The gateway probe sends two bounded ICMP requests only to the configured default gateway. DNS and HTTP probes are optional and run only when the user enters a name or endpoint; WAID has no hard-coded public endpoint. Probe timeouts accept 250–15,000 milliseconds, cancellation is propagated, and there is no retry loop.

Diagnoses distinguish the local stack, adapter, LAN gateway, DNS, internet/application path, proxy, VPN, Wi-Fi, and firewall layers. Failed tests retain source references, latency, loss, and confidence. DNS or Winsock resets are recommended only when matching evidence exists, and execution remains in WAID's separate administrator and explicit-approval repair workflow.

Before SQLite persistence or JSON export, IP addresses are reduced to subnet prefixes, proxy credentials and bypass lists are excluded, user names are redacted, and probe response bodies are never collected. The export contains the normalized snapshot, test metadata, topology, findings, limitations, and no browser data or traffic content.

Provider availability and ICMP behavior vary with Windows edition, policy, firewall, VPN software, captive portals, localization, and network hardware. A timeout is reported separately from a definitive failure and unavailable data is never treated as healthy.
