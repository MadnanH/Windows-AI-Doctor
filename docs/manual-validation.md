# Real Windows manual validation

Publish WAID for the target architecture, then run each scenario on a real Windows installation. These scripts launch the supplied executable, enforce the scenario's operating-system or elevation precondition, prompt the operator through the same acceptance checks, and write a timestamped JSON result under `artifacts/manual-validation`.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\publish.ps1 -Platform x64
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Windows10.ps1 -ApplicationPath <published-WAID.exe>
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Windows11.ps1 -ApplicationPath <published-WAID.exe>
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Administrator.ps1 -ApplicationPath <published-WAID.exe>
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-StandardUser.ps1 -ApplicationPath <published-WAID.exe>
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Offline.ps1 -ApplicationPath <published-WAID.exe>
```

Windows 10 and Windows 11 results must come from separate machines or virtual machines. Run the administrator and standard-user scenarios in the corresponding security contexts. Before the offline scenario, disconnect network adapters or isolate the virtual machine; the script intentionally does not change host networking. Never approve a destructive repair on a production machine—use a disposable VM with a snapshot and choose the lowest-risk applicable repair.

Attach the five JSON reports to the release-validation record. A scenario is accepted only when every check is marked `pass`; the script exits with code 1 when any check fails.
