# WAID Command-Line Interface

Version 0.36 introduces `waid.exe`, a thin automation surface over the same scanners, persistence, policy, plugin, report, timeline, and repair-orchestration services used by the desktop application. It reads and writes the installed WAID data store under the current Windows user profile. It does not contain alternate scanner or repair implementations.

## Usage

```text
waid <command> [options] [--json|--human]
```

Use `waid help` for built-in help. Human output is the default. `--json` emits one `waid-cli-1.0` envelope to stdout; progress goes only to stderr, so stdout can be piped safely. Ctrl+C requests cooperative cancellation.

## Commands

| Command | Purpose | Options |
|---|---|---|
| `version` | Application, CLI contract, and runtime versions | output format |
| `status` | Database health, registered modules, effective policy | output format |
| `scan` | Run and persist the production scanner plan | output format |
| `findings` | Read recent persisted findings | `--limit 1..500` |
| `report` | Read the latest diagnosis | `--export package` for a redacted ZIP |
| `timeline` | Read persisted reliability events | `--limit 1..200` |
| `policy` | Explain effective enterprise rules and locks | `--refresh` |
| `plugins` | List certified plugin inventory and state | output format |
| `repair-plan list` | List recent durable repair plans | `--limit 1..100` |
| `repair-plan simulate` | Create a read-only simulation | `--repair REPAIR_ID` |
| `repair-plan execute` | Execute an already simulated current plan | safety options below |

Examples:

```powershell
waid status --json
waid scan --human
waid findings --limit 25 --json
waid report --export package --json
waid policy --refresh --json
waid repair-plan simulate --repair dism-repair --json
```

## Repair execution safety

Simulation never runs a system command. Execution requires all three independent, plan-bound inputs:

```powershell
waid repair-plan execute --id 01234567-89ab-cdef-0123-456789abcdef --approve --acknowledge-risk --confirmation "EXECUTE WAID REPAIR 01234567-89ab-cdef-0123-456789abcdef"
```

The plan must still exist and be awaiting approval. Enterprise policy, administrator checks, safety scoring, backups, restore points, validation, and rollback gates remain enforced by the shared repair orchestrator. CLI use never authorizes background or automatic repairs. Standard-user read-only commands remain available; a repair that needs elevation fails with an actionable permission result.

## JSON contract

The schema is [waid-cli-output-v1.schema.json](schemas/waid-cli-output-v1.schema.json). Every completed command contains `schemaVersion`, `command`, `succeeded`, `exitCode`, `completedAtUtc`, and either `data` or `error`. Consumers must ignore additive fields and check both schema version and exit code.

Stable exit codes:

| Code | Meaning |
|---:|---|
| 0 | Success |
| 2 | Invalid command or option |
| 3 | Requested record not found |
| 4 | Blocked by enterprise policy |
| 5 | Windows permission denied |
| 6 | Cancelled |
| 7 | State or confirmation conflict |
| 8 | Operation failed safely |
| 10 | Unexpected internal failure |

## Privacy and audit

CLI commands use the existing SQLite repositories and append-only audit service. Audit entries identify the actor as `Cli` and record the command and outcome, but not raw arguments, confirmation phrases, secrets, or tokens. Status and plugin output omit local database and plugin paths. Diagnostic package export uses the existing policy-aware redaction pipeline and replaces the user-profile portion of the returned path.

The CLI does not expose passwords, product keys, tokens, browser data, personal file contents, or unnecessary serial numbers. It works offline; commands that inspect unavailable Windows APIs return the same explicit unavailable or degraded states as the desktop app.

## Publishing

`publish.ps1` publishes the desktop application and the matching CLI for `win-x64` or `win-arm64`. The CLI is placed in the `cli` directory inside the selected installed or portable artifact. The executable and desktop app must be kept at the same version.

## Limitations

The CLI controls the installed/current-user WAID workspace only and is local-machine automation, not a remote-management endpoint. It does not provide interactive diagnosis authoring or plugin installation. Real repairs require Windows, explicit approval, applicable administrator access, and any OS capabilities required by that repair.