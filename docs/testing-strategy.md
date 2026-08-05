# WAID Automated Test Strategy and Quality Gates

WAID uses deterministic, layered verification. Ordinary development and CI never run destructive Windows repairs. The authoritative inventory is [`tests/test-catalog.json`](../tests/test-catalog.json); every non-destructive layer blocks the GitHub Actions **Required release gate**.

## Test layers

| Layer | Purpose | Environment | Gate |
|---|---|---|---|
| Unit | Domain invariants, application orchestration, diagnosis/ranking algorithms | .NET 8 | Blocking |
| Integration | SQLite migrations/repositories, composition, plugins, exports, offline workflows | Windows CI with isolated workspaces | Blocking |
| Windows integration | Windows-targeted adapters and provider result handling | Windows CI; real-device matrix separately | Blocking |
| UI | XAML parsing, navigation, automation IDs, theme/accessibility rules | Windows CI; assistive technology manually | Blocking |
| Security | Redaction, archive attacks, encryption, plugin trust, policy and repair gates | Windows CI | Blocking |
| Performance | Bounded aggregation/downsampling and resource-policy invariants without machine-speed assertions | Windows CI | Blocking |
| Packaging | Desktop/CLI x64 publish, ARM64 compile, MSIX metadata, signing-material checks | Windows CI | Blocking |
| Architecture | Dependency direction, acyclic graph, composition, no service locator | Windows CI | Blocking |
| Critical path | Scan -> Diagnose -> Recommend -> Confirm -> Repair -> Verify with controlled adapters | Windows CI | Blocking |
| Destructive VM | Real restore point, backup, repair, restart, validation, rollback | Disposable snapshotted Windows VM only | Never ordinary CI |

Traits use `Category` with values in `WaidTestCategories`. CI runs the complete suite as well as focused critical gates, so an omitted trait cannot hide a regression.

## Determinism and isolation

Automated tests must not use `Thread.Sleep`, fixed real-duration delays, network services, the user's WAID database, or mutable machine-wide state. `Test-QualityPolicy.ps1` rejects real sleeps. Timeout behavior waits indefinitely for cancellation; concurrency uses `AsyncTestGate`; time-sensitive services use `FixedTimeProvider`.

`IsolatedTestWorkspace` creates a unique operating-system temporary directory, rejects rooted paths and traversal, provides pooling-disabled SQLite connection strings, and confines cleanup to its verified test root. Existing tests that create temporary databases follow the same GUID-qualified, pooling-disabled, `finally`-cleanup pattern. Results and coverage use ignored `artifacts/test-results`; CI uploads diagnostics even after failure.

No production test hook is enabled or required. Windows behavior remains behind injectable interfaces; controlled adapters are used unless a test is explicitly assigned to the Windows or destructive layer.

## Local gates

```powershell
./build.ps1 -Configuration Release
dotnet test WindowsAIDoctor.sln -c Release --no-build --filter "Category=CriticalPath|Category=Security"
dotnet test tests/WAID.Infrastructure.Tests/WAID.Infrastructure.Tests.csproj -c Release --no-build --filter "Category=Architecture"
dotnet test tests/WAID.Infrastructure.Tests/WAID.Infrastructure.Tests.csproj -c Release --no-build --filter "Category=WindowsIntegration"
dotnet test tests/WAID.Application.Tests/WAID.Application.Tests.csproj -c Release --no-build --filter "Category=Performance"
pwsh scripts/Test-QualityPolicy.ps1
pwsh scripts/Test-Packaging.ps1
```

The standard build restores, builds with warnings as errors, runs every non-destructive test, validates quality policy and knowledge, parses XAML/navigation accessibility contracts, and validates packaging metadata.

Coverage commands:

```powershell
dotnet test WindowsAIDoctor.sln -c Release --no-build --filter "Category!=DestructiveVm" --collect "XPlat Code Coverage" --results-directory artifacts/test-results/coverage
pwsh scripts/Test-CoverageGate.ps1 -ResultsDirectory artifacts/test-results/coverage -MinimumLinePercent 60
```

Sixty percent aggregate line coverage is the initial regression floor for this mature mixed Windows/UI codebase, not a quality claim or target. It may only increase unless a documented reviewed exception changes it. Critical safety behavior has explicit tests regardless of aggregate percentage.

## Failure and flaky-test policy

CI writes TRX diagnostics per layer and has a five-minute integration hang detector. It never automatically retries failures. Known flaky tests must be listed in `tests/flaky-tests.json` with exact name, owner, issue, reason, and UTC expiry. Expired or incomplete entries fail the gate. The registry is currently empty; quarantine cannot excuse critical-path or security failures.

## Destructive VM authorization

`Invoke-DestructiveVmTests.ps1` requires Windows, elevation, `WAID_ALLOW_DESTRUCTIVE_VM_TESTS=1`, disposable-VM and snapshot acknowledgements, a snapshot ID, and the exact separate phrase `RUN DESTRUCTIVE VM TEST`. It runs only tests carrying `Category=DestructiveVm`.

No destructive VM tests are registered in this release, so the script fails safely rather than performing an action. Real-hardware and disposable-VM evidence continues through `Run-WaidManualValidation.ps1` and is never fabricated by CI.

## Release decision and boundaries

The CI release gate depends on unit, integration, critical/security, Windows integration, quality/UI/performance, coverage, packaging, and secret-scanning jobs. Any failure blocks it. Branch protection should require **Required release gate** and secret scanning.

Windows CI cannot certify consumer Windows editions, ARM64 hardware, SMART/NVMe vendors, firmware, Defender policy, restore points, or rollback under interruption. Those remain in the documented Windows 10/11, administrator/standard-user, offline, unsupported-hardware, accessibility, and destructive-VM manual matrix. Production signing remains a protected release-environment operation.
