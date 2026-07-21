[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Windows10','Windows11','Administrator','StandardUser','Offline')][string]$Scenario,
    [Parameter(Mandatory)][string]$ApplicationPath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\manual-validation')
)
$ErrorActionPreference = 'Stop'
$resolvedApplication = (Resolve-Path -LiteralPath $ApplicationPath).Path
$version = [Environment]::OSVersion.Version
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$isAdministrator = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($Scenario -eq 'Windows10' -and $version.Build -ge 22000) { throw "Windows 10 is required; detected build $($version.Build)." }
if ($Scenario -eq 'Windows11' -and $version.Build -lt 22000) { throw "Windows 11 is required; detected build $($version.Build)." }
if ($Scenario -eq 'Administrator' -and -not $isAdministrator) { throw 'Run this script from an elevated PowerShell session.' }
if ($Scenario -eq 'StandardUser' -and $isAdministrator) { throw 'Run this script from a non-elevated PowerShell session.' }

$checks = @(
    'Application opens without an error dialog',
    'Dashboard scan shows scanner names, progress, and completion',
    'Cancel stops an active scan and displays Scan cancelled',
    'Scanner failures are visible while remaining scanners continue',
    'AI Diagnosis shows the latest persisted scan evidence',
    'Health Dashboard shows overall and category scores',
    'Evidence Viewer shows the findings used by diagnosis',
    'Recommended Repairs shows diagnosis-backed recommendations',
    'History reloads saved scans and repair attempts',
    'Diagnostics export creates a ZIP with logs, scan data, diagnosis, repair history, version, and non-sensitive system data',
    'Closing and reopening the app preserves saved history'
)
if ($Scenario -in 'Administrator','StandardUser') { $checks += 'Cancel the repair confirmation dialog and verify no repair starts' }
if ($Scenario -eq 'Administrator') { $checks += 'After explicit approval, a low-risk repair performs administrator and safeguard checks before execution' }
if ($Scenario -eq 'StandardUser') { $checks += 'After explicit approval, an administrator-required repair is rejected without execution' }
if ($Scenario -eq 'Offline') { $checks += 'With network access disabled, scan and diagnosis complete without attempting cloud access' }

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$process = Start-Process -FilePath $resolvedApplication -PassThru
Write-Host "WAID started with process id $($process.Id). Complete each check in the application."
$results = foreach ($check in $checks) {
    do { $answer = (Read-Host "$check [pass/fail]").Trim().ToLowerInvariant() } until ($answer -in 'pass','fail')
    [pscustomobject]@{ Check = $check; Result = $answer }
}
$report = [pscustomobject]@{
    Scenario = $Scenario
    TimestampUtc = [DateTimeOffset]::UtcNow
    OperatingSystem = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    OSBuild = $version.Build
    IsAdministrator = $isAdministrator
    ApplicationPath = $resolvedApplication
    Results = $results
}
$destination = Join-Path $OutputDirectory ("{0}-{1:yyyyMMdd-HHmmss}.json" -f $Scenario,[DateTime]::UtcNow)
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $destination -Encoding utf8
Write-Host "Validation report: $destination"
if ($results.Result -contains 'fail') { exit 1 }
