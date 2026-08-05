# Remote Diagnostic Package and Case Exchange

WAID 0.37 creates encrypted, redacted offline `.waidcase` packages for technician review. The workflow is available from **Case Exchange** in the main navigation. It works without a network connection and uses the same enterprise export policy, repositories, redaction rules, and append-only audit trail as the rest of WAID.

## Export workflow

The wizard requires the user to:

1. Select scans/findings/crash metadata, diagnosis, timeline, repair history, sanitized logs, system summary, and optional notes.
2. Choose Standard or Maximum redaction and inspect the content/redaction preview.
3. enter a 12–256 character package password and explicitly export.

The password is never persisted or written to logs/audit. It should be delivered to the technician through a different communication channel from the package.

Standard redaction removes secret-named fields and values including passwords, tokens, authorization values, cookies, product keys, serial/device identifiers, the Windows user name, and the user-profile path. Maximum redaction additionally removes path, command, account, host, machine, IP, and MAC fields. Both profiles always exclude:

- Full dump and minidump file contents
- Browser history and browser data
- Passwords, credentials, tokens, and product keys
- Personal files and document contents
- Raw unrestricted logs and audit files
- Unnecessary hardware serial numbers

## Package format version 1

The binary envelope begins with ASCII magic `WAIDCASE1`, followed by a little-endian 32-bit JSON-header length, a bounded JSON encryption header, ciphertext, and a 16-byte authentication tag. Version 1 uses:

- AES-256-GCM authenticated encryption
- A random 128-bit salt and 96-bit nonce per export
- PBKDF2-HMAC-SHA-256 with 210,000 iterations
- `WAIDCASE1` as authenticated associated data

The decrypted payload is a ZIP archive used only as a bounded container. `manifest.json` declares the format, schema version, application version, UTC creation time, redaction profile, included categories, privacy notice, and the byte length and SHA-256 hash of every content entry. Content documents are UTF-8 JSON at the archive root.

## Safe import and strict review mode

Import does not extract files. WAID decrypts into bounded memory, authenticates the complete envelope, then validates the archive before exposing any document. Validation rejects:

- Incorrect passwords or any authenticated-envelope modification
- Missing, duplicate, rooted, nested, traversal, or non-JSON entry paths
- More than 32 entries
- Encrypted packages over 64 MiB
- Individual expanded entries over 10 MiB
- Total expanded content over 50 MiB
- Compression ratios over 100:1
- Missing/extra manifest entries, invalid hashes, malformed JSON, or unsupported schema versions

Only after all entries pass validation are cloned JSON values shown under the permanent **UNTRUSTED REVIEW-ONLY CASE** banner. Imported documents remain in temporary memory and are never inserted into SQLite, merged with local evidence, loaded as plugins, interpreted as commands, or passed to repair approval/execution. Closing the page or application discards the review.

Package export and validation attempts are security-audited without passwords, notes, document contents, or full local paths. Integrity failure blocks the entire import; partial review is forbidden.

## Compatibility and recovery

WAID currently accepts manifest schema version 1 only. A future schema fails with `WAID-CASE-INCOMPATIBLE`; the sender must export with a compatible WAID version. Authentication failure deliberately does not distinguish a wrong password from tampering. Corrupt or unsafe packages should be discarded and recreated by the source.

The feature requires no administrator access. Organization policy may disable both export and review import through the existing Exports capability. No database migration is required because external case content is intentionally not persisted.

## Limitations

The package is an offline handoff, not remote access, synchronization, or a support tunnel. WAID does not manage password delivery, case assignment, signatures from an external certificate authority, or long-term imported-case storage. Encryption protects confidentiality and integrity when a strong password is exchanged separately; it does not establish the human identity of the sender.
