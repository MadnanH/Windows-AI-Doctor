# Windows Security Posture Analyzer

Security Posture complements, and does not replace, Windows Security. It reads supported local state for Microsoft Defender Antivirus, Windows Firewall profiles, Secure Boot, TPM readiness, operating-system volume encryption, Core Isolation/HVCI, UAC, SmartScreen, Credential Guard, Windows Update handoff, and policy sources. It never disables, bypasses, or weakens a control.

Controls use five explicit states: Enabled, Disabled, Unknown, Unavailable, and Not Applicable. Only a confirmed Disabled state creates a finding. Provider failure, insufficient permission, unsupported hardware, and edition limitations never become “disabled.” Contradictory or incomplete signals remain Unknown/Unavailable.

Policy-managed findings show the policy source and instruct the user to contact the administrator; WAID never offers a bypass. Other remediation is a preview that points to supported Windows Security or Settings surfaces and lists hardware, edition, virtualization, TPM, administrator, or restart prerequisites. Language reports observed state without claiming compromise or replacing antivirus judgment.

Snapshots, findings, policy source, and acknowledged exceptions are stored in schema 14. User names are redacted; no recovery keys, credentials, secrets, tokens, product keys, or personal files are collected.

Limitations: Windows edition, third-party antivirus registration, firmware, virtualization, domain/MDM policy, and standard-user permissions affect provider availability. Windows 10/11, managed devices, Secure Boot/TPM variants, standard-user, and administrator scenarios still require real-hardware validation.
