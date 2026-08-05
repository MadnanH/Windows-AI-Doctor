$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot '..\src\WAID.Desktop'
$main=Get-Content -Raw (Join-Path $root 'MainWindow.xaml')
@('NavDashboard','NavTechnician','NavDrivers','NavBoot','NavUpdates','NavStorage','NavSecurity','NavNetwork','NavEvidence','NavKnowledge','NavDiagnosis','NavChat','NavHealth','NavPredictive','NavLiveMonitoring','NavReliabilityTimeline','NavPerformanceHistory','NavDigitalTwin','NavNotifications','NavOperations','NavCaseExchange','NavRepairs','NavRepairLifecycle','NavHistory','NavAudit','NavEnterprisePolicy','NavPlugins')|ForEach-Object{if($main-notmatch$_){throw "Missing automation id $_"}}
if($main-notmatch'WorkspaceIndicator'){throw 'Missing portable workspace indicator automation id'}
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
$repair=Get-Content -Raw (Join-Path $root 'Views\RepairOrchestrationPage.xaml')
@('RepairWorkflowScreen','RepairCurrentAction','RepairTechnicalView','RepairDefinition','SimulateRepairButton','RepairApprovalCheck','RepairRiskAcknowledgement','ExecuteRepairLifecycleButton','CancelRepairLifecycle','RecoverRepairLifecycle','RepairWorkflowStatus','RollbackApprovalCheck','RollbackRecoveryArtifactButton','RefreshRepairLifecycle')|ForEach-Object{if($repair-notmatch$_){throw "Missing repair workflow accessibility id $_"}}
if($repair-match'#[0-9A-Fa-f]{6,8}'){throw 'Repair workflow uses a hard-coded color instead of theme resources'}
$history=Get-Content -Raw (Join-Path $root 'Views\HistoryPage.xaml')
@('HistoryTitle','RepairHistoryFilter','RepairOutcomeFilter','RefreshHistory','ExportRepairAudit','RepairOutcomeAggregates','RepairAuditChain')|ForEach-Object{if($history-notmatch$_){throw "Missing repair history accessibility id $_"}}
Write-Host 'Accessibility navigation and recovery smoke checks passed.'
$evidence=Get-Content -Raw (Join-Path $root 'Views\EvidenceExplorerPage.xaml')
@('EvidenceExplorerTitle','RefreshEvidenceGraph','EvidenceDomainFilter','EvidenceViewMode','EvidenceNodeList','EvidenceRelationshipList')|ForEach-Object{if($evidence-notmatch$_){throw "Missing evidence accessibility id $_"}}
$knowledge=Get-Content -Raw (Join-Path $root 'Views\KnowledgePage.xaml')
@('KnowledgeTitle','KnowledgeQuery','KnowledgeSearch','KnowledgeStatus','KnowledgeResults')|ForEach-Object{if($knowledge-notmatch$_){throw "Missing knowledge accessibility id $_"}}

$plugins=Get-Content -Raw (Join-Path $root 'Views\PluginsPage.cs')
@('PluginsPageTitle','PluginInstallPath','PreviewPluginInstall','ApprovePluginPermissions','InstallPluginFile','PluginManagerStatus')|ForEach-Object{if($plugins-notmatch$_){throw "Missing plugin manager accessibility id $_"}}

$enterprisePolicy=Get-Content -Raw (Join-Path $root 'Views\EnterprisePolicyPage.xaml')
@('EnterprisePolicyTitle','EnterprisePolicyStatus','RefreshEnterprisePolicy','EnterprisePolicyRules','EnterprisePolicyHistory')|ForEach-Object{if($enterprisePolicy-notmatch$_){throw "Missing enterprise policy accessibility id $_"}}

$caseExchange=Get-Content -Raw (Join-Path $root 'Views\CaseExchangePage.xaml')
@('CaseExchangeTitle','CaseNotes','PreviewCasePackage','CasePassword','ExportCasePackage','ImportCasePath','ImportCasePackage','CaseExchangeStatus')|ForEach-Object{if($caseExchange-notmatch$_){throw "Missing case exchange accessibility id $_"}}