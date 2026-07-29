$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot '..\src\WAID.Desktop'
$main=Get-Content -Raw (Join-Path $root 'MainWindow.xaml')
@('NavDashboard','NavDrivers','NavBoot','NavUpdates','NavStorage','NavSecurity','NavNetwork','NavEvidence','NavKnowledge','NavDiagnosis','NavChat','NavHealth','NavPredictive','NavLiveMonitoring','NavReliabilityTimeline','NavPerformanceHistory','NavDigitalTwin','NavNotifications','NavOperations','NavRepairs','NavHistory','NavAudit','NavPlugins')|ForEach-Object{if($main-notmatch$_){throw "Missing automation id $_"}}
$app=Get-Content -Raw (Join-Path $root 'App.xaml.cs')
if($app-notmatch'StartupRecoveryTitle'){throw 'Missing startup recovery automation id'}
$settings=Get-Content -Raw (Join-Path $root 'Views\SettingsPage.xaml')
@('SettingsSearch','ImportSettingsProfile','ExportSettingsProfile','ResetSettings','RefreshDatabaseHealth','BackupDatabase','RestoreDatabase')|ForEach-Object{if($settings-notmatch$_){throw "Missing settings automation id $_"}}
$dashboard=Get-Content -Raw (Join-Path $root 'Views\DashboardPage.xaml')
if($dashboard-notmatch'ScannerPlan'){throw 'Missing scanner plan automation id'}
@('DriverHealthPage.xaml','BootHealthPage.xaml','UpdateHealthPage.xaml','StorageCenterPage.xaml','SecurityCenterPage.xaml','NetworkHealthPage.xaml','ChatPage.xaml','EvidenceExplorerPage.xaml','KnowledgePage.xaml')|ForEach-Object{[xml](Get-Content -Raw (Join-Path $root "Views\$_"))|Out-Null}
$security=Get-Content -Raw (Join-Path $root 'Views\SecurityCenterPage.xaml')
@('SecurityCenterTitle','AnalyzeSecurityButton')|ForEach-Object{if($security-notmatch$_){throw "Missing security accessibility id $_"}}
$chat=Get-Content -Raw (Join-Path $root 'Views\ChatPage.xaml')
@('ChatTitle','ChatStatus','ChatMessages','ChatQuestion','ChatSendButton')|ForEach-Object{if($chat-notmatch$_){throw "Missing chat accessibility id $_"}}
$network=Get-Content -Raw (Join-Path $root 'Views\NetworkHealthPage.xaml')
@('NetworkHealthTitle','NetworkDnsName','NetworkHttpEndpoint','RunNetworkDiagnostics','CancelNetworkDiagnostics','ExportNetworkDiagnostics','NetworkStatus')|ForEach-Object{if($network-notmatch$_){throw "Missing network accessibility id $_"}}
Get-ChildItem (Join-Path $root 'Views') -Filter '*.xaml'|ForEach-Object{[xml](Get-Content -Raw $_.FullName)|Out-Null}
Write-Host 'Accessibility navigation and recovery smoke checks passed.'
$evidence=Get-Content -Raw (Join-Path $root 'Views\EvidenceExplorerPage.xaml')
@('EvidenceExplorerTitle','RefreshEvidenceGraph','EvidenceDomainFilter','EvidenceViewMode','EvidenceNodeList','EvidenceRelationshipList')|ForEach-Object{if($evidence-notmatch$_){throw "Missing evidence accessibility id $_"}}
$knowledge=Get-Content -Raw (Join-Path $root 'Views\KnowledgePage.xaml')
@('KnowledgeTitle','KnowledgeQuery','KnowledgeSearch','KnowledgeStatus','KnowledgeResults')|ForEach-Object{if($knowledge-notmatch$_){throw "Missing knowledge accessibility id $_"}}
