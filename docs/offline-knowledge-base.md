# Offline Knowledge Base: Authoring, Trust, Updates, and Licensing

WAID knowledge catalog version `2026.07` and index schema `1.0` are embedded with the application and operate without network access. Articles include title, summary, body, error codes, symptoms, prerequisites, risks, supported Windows versions, optional repair guidance, references, source, license, trust level, content checksum metadata, publication date, and review date.

## Authoring

Articles must be concise factual summaries, cite at least one stable reference, list applicable Windows versions, disclose prerequisites and risks, and avoid commands or executable scripts. Content containing prompt-injection phrases, script markup, encoded PowerShell, or instruction-like execution text is rejected. IDs are immutable and unique. Changes require a new knowledge version and review date.

## Trust

- `Official`: based on first-party vendor documentation and reviewed by WAID Engineering.
- `Curated`: authored and reviewed by WAID Engineering from disclosed references.
- `Community`: informational and visibly labeled; cannot define repair guidance.
- `Plugin`: extension-provided and visibly labeled; cannot define repair guidance.

Only Official and Curated articles may expose guidance to the repair-preview layer. Even trusted guidance cannot execute, approve, register, or alter a repair module. Existing repair policy, elevation, backup, restore-point, and explicit approval gates remain authoritative.

## Retrieval and compatibility

Local ranking combines title/body relevance, exact saved-evidence matches, supported Windows version, trust, and review freshness. Incompatible content receives a strong penalty and may be omitted. Results display source, license, trust label, applicability, and linked finding relevance. Chat receives retrieved articles only as citeable evidence and continues to treat all evidence as data rather than instructions.

## Index updates and recovery

The index is built in a sibling temporary file and atomically replaced. It stores catalog/index versions and a SHA-256 checksum over all article contracts. A corrupt or mismatched index produces a typed failure; application startup rebuilds it from the validated embedded catalog. No downloader, remote endpoint, telemetry call, or silent update mechanism exists.

Future knowledge updates must ship through the normal signed application release or an explicitly trusted plugin policy. Update review must verify references, licensing, compatibility, hostile-content checks, repair boundaries, checksums, and regression tests.

## Licensing

Every article declares its source and license. Microsoft Learn-derived material is summarized rather than copied and identifies the documentation source; WAID-authored wording remains under the product license. Authors must not add content without a license compatible with commercial redistribution and attribution obligations.