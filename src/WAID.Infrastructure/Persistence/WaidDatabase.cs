using Microsoft.Data.Sqlite;

namespace WAID.Infrastructure.Persistence;

public sealed class WaidPersistenceException(string code, string userMessage, string recoveryAction, Exception? innerException = null)
    : InvalidOperationException(userMessage, innerException)
{
    public string Code { get; } = code;
    public string UserMessage { get; } = userMessage;
    public string RecoveryAction { get; } = recoveryAction;
}

public sealed record DatabaseMigrationStatus(int FromVersion, int ToVersion, DateTimeOffset CompletedAtUtc, bool Succeeded, string Detail);

public sealed class WaidDatabase
{
    public const int CurrentSchemaVersion = 24;
    public const int WaidApplicationId = 1463896388;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);

    public WaidDatabase(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource) || builder.DataSource == ":memory:")
            throw new ArgumentException("A file-backed SQLite data source is required.", nameof(connectionString));
        DatabasePath = Path.GetFullPath(builder.DataSource);
        builder.DataSource = DatabasePath;
        builder.ForeignKeys = true;
        builder.Pooling = false;
        _connectionString = builder.ToString();
    }

    public string DatabasePath { get; }
    public DatabaseMigrationStatus? LastMigrationStatus { get; private set; }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    public async Task InitializeAsync(CancellationToken token)
    {
        await _initializationGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            await using var connection = OpenConnection();
            await EnsureHealthyAsync(connection, token).ConfigureAwait(false);
            var version = await ReadVersionAsync(connection, token).ConfigureAwait(false);
            if (version > CurrentSchemaVersion)
                throw new WaidPersistenceException("WAID-DB-NEWER", $"Database schema {version} is newer than supported schema {CurrentSchemaVersion}.", "Install a newer WAID version. The database was not modified.");

            await ConfigureRecoveryAsync(connection, token).ConfigureAwait(false);
            if (version == CurrentSchemaVersion)
            {
                await ValidateSchemaAsync(connection, token).ConfigureAwait(false);
                LastMigrationStatus = await ReadLastMigrationAsync(connection, token).ConfigureAwait(false)
                    ?? new(version, version, DateTimeOffset.UtcNow, true, "Schema is current.");
                return;
            }

            if (version > 0) await CreateMigrationBackupAsync(connection, version, token).ConfigureAwait(false);
            var startingVersion = version;
            try
            {
                foreach (var migration in Migrations.Where(item => item.Version > version).OrderBy(item => item.Version))
                {
                    token.ThrowIfCancellationRequested();
                    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token).ConfigureAwait(false);
                    try
                    {
                        await ExecuteAsync(connection, transaction, migration.Sql, token).ConfigureAwait(false);
                        await ExecuteAsync(connection, transaction, $"PRAGMA user_version={migration.Version};", token).ConfigureAwait(false);
                        if (migration.Version >= 7)
                            await ExecuteAsync(connection, transaction,
                                "INSERT INTO schema_migrations(version,applied_utc,description) VALUES($version,$time,$description) ON CONFLICT(version) DO NOTHING;", token,
                                ("$version", migration.Version), ("$time", DateTimeOffset.UtcNow.ToString("O")), ("$description", migration.Description)).ConfigureAwait(false);
                        await transaction.CommitAsync(token).ConfigureAwait(false);
                        version = migration.Version;
                    }
                    catch
                    {
                        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                        throw;
                    }
                }

                await ValidateSchemaAsync(connection, token).ConfigureAwait(false);
                LastMigrationStatus = new(startingVersion, version, DateTimeOffset.UtcNow, true, $"Migrated schema from {startingVersion} to {version}.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not WaidPersistenceException)
            {
                LastMigrationStatus = new(startingVersion, version, DateTimeOffset.UtcNow, false, "Migration failed and the active step was rolled back.");
                throw new WaidPersistenceException("WAID-DB-MIGRATION", "The local database could not be upgraded safely.", "Restart WAID. If the problem continues, use Database Recovery or contact support.", exception);
            }
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 11 or 26)
        {
            throw new WaidPersistenceException("WAID-DB-CORRUPT", "The local database is damaged or is not a valid WAID database.", "Open Database Recovery and restore a verified backup.", exception);
        }
        finally { _initializationGate.Release(); }
    }

    private static async Task EnsureHealthyAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        var result = Convert.ToString(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new WaidPersistenceException("WAID-DB-CORRUPT", "The local database failed its integrity check.", "Open Database Recovery and restore a verified backup.");
    }

    private static async Task ConfigureRecoveryAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA wal_autocheckpoint=1000; PRAGMA application_id={WaidApplicationId};";
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private async Task CreateMigrationBackupAsync(SqliteConnection source, int version, CancellationToken token)
    {
        var directory = Path.Combine(Path.GetDirectoryName(DatabasePath)!, "Backups", "Database");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"waid-pre-migration-v{version}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.db");
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        await destination.OpenAsync(token).ConfigureAwait(false);
        source.BackupDatabase(destination);
        foreach (var expired in new DirectoryInfo(directory).GetFiles("waid-pre-migration-*.db").OrderByDescending(item => item.CreationTimeUtc).Skip(5)) expired.Delete();
    }

    private static async Task<int> ReadVersionAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
    }

    private static async Task<DatabaseMigrationStatus?> ReadLastMigrationAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version,applied_utc,description FROM schema_migrations ORDER BY version DESC LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false)
            ? new(reader.GetInt32(0) - 1, reader.GetInt32(0), DateTimeOffset.Parse(reader.GetString(1)), true, reader.GetString(2))
            : null;
    }

    private static async Task ValidateSchemaAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(token).ConfigureAwait(false)) actual.Add(reader.GetString(0));
        var missing = RequiredTables.Where(table => !actual.Contains(table)).ToArray();
        if (missing.Length > 0)
            throw new WaidPersistenceException("WAID-DB-SCHEMA", "The local database schema is incomplete.", "Restore a verified database backup or contact support.");
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken token, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private sealed record Migration(int Version, string Description, string Sql);
    private static readonly string[] RequiredTables = ["scan_sessions", "findings", "settings", "repair_history", "diagnosis_reports", "health_snapshots", "scan_schedule", "repair_approvals", "evidence", "rollback_records", "timeline_events", "metrics", "chats", "policies", "plugins", "alerts", "reports", "audit_events", "schema_migrations", "configuration_state", "scanner_executions", "driver_analysis_runs", "boot_analysis_runs", "windows_update_analysis_runs", "storage_health_runs", "security_posture_runs", "chat_conversations", "network_health_runs", "evidence_graph_runs", "evidence_graph_nodes", "evidence_graph_edges", "repair_recommendation_runs", "predictive_health_runs", "monitoring_sessions", "monitoring_samples", "monitoring_gaps", "monitoring_collector_failures", "monitoring_retention", "timeline_incidents", "timeline_projection_state", "performance_samples", "performance_rollups", "performance_retention_jobs", "digital_twin_snapshots", "digital_twin_diffs", "digital_twin_retention_jobs", "scheduled_scan_history"];
    private static readonly Migration[] Migrations =
    [
        new(1, "Core scans, evidence, and settings", """
            CREATE TABLE IF NOT EXISTS scan_sessions(id TEXT PRIMARY KEY, started_utc TEXT NOT NULL, completed_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS findings(id TEXT PRIMARY KEY, session_id TEXT NOT NULL, scanner_id TEXT NOT NULL, code TEXT NOT NULL, title TEXT NOT NULL, description TEXT NOT NULL, severity INTEGER NOT NULL, repair_id TEXT NULL, evidence_json TEXT NOT NULL, FOREIGN KEY(session_id) REFERENCES scan_sessions(id) ON DELETE CASCADE);
            CREATE INDEX IF NOT EXISTS ix_findings_session ON findings(session_id);
            CREATE TABLE IF NOT EXISTS settings(id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL, updated_utc TEXT NOT NULL);
            """),
        new(2, "Repair history", """
            CREATE TABLE IF NOT EXISTS repair_history(transaction_id TEXT PRIMARY KEY,repair_id TEXT NOT NULL,status INTEGER NOT NULL,created_utc TEXT NOT NULL,completed_utc TEXT NULL,summary TEXT NULL,details TEXT NULL,backup_location TEXT NULL,restore_point_description TEXT NULL,events_json TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_repair_history_created ON repair_history(created_utc DESC);
            """),
        new(3, "Diagnosis reports", """
            CREATE TABLE IF NOT EXISTS diagnosis_reports(id TEXT PRIMARY KEY,scan_session_id TEXT NOT NULL,generated_utc TEXT NOT NULL,report_json TEXT NOT NULL,FOREIGN KEY(scan_session_id) REFERENCES scan_sessions(id) ON DELETE CASCADE);
            CREATE INDEX IF NOT EXISTS ix_diagnosis_reports_generated ON diagnosis_reports(generated_utc DESC);
            """),
        new(4, "Health snapshots", """
            CREATE TABLE IF NOT EXISTS health_snapshots(id TEXT PRIMARY KEY,captured_utc TEXT NOT NULL,snapshot_json TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_health_snapshots_captured ON health_snapshots(captured_utc DESC);
            """),
        new(5, "Scan scheduling", "CREATE TABLE IF NOT EXISTS scan_schedule(id INTEGER PRIMARY KEY CHECK(id=1),schedule_json TEXT NOT NULL);"),
        new(6, "Repair approvals", """
            CREATE TABLE IF NOT EXISTS repair_approvals(id TEXT PRIMARY KEY,requested_utc TEXT NOT NULL,approval_json TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_repair_approvals_requested ON repair_approvals(requested_utc DESC);
            """),
        new(7, "Commercial persistence catalog", """
            CREATE TABLE IF NOT EXISTS evidence(id TEXT PRIMARY KEY,scan_session_id TEXT NULL,source TEXT NOT NULL,captured_utc TEXT NOT NULL,evidence_json TEXT NOT NULL,FOREIGN KEY(scan_session_id) REFERENCES scan_sessions(id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS rollback_records(id TEXT PRIMARY KEY,repair_transaction_id TEXT NOT NULL,created_utc TEXT NOT NULL,record_json TEXT NOT NULL,FOREIGN KEY(repair_transaction_id) REFERENCES repair_history(transaction_id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS timeline_events(id TEXT PRIMARY KEY,occurred_utc TEXT NOT NULL,category TEXT NOT NULL,event_json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS metrics(id TEXT PRIMARY KEY,captured_utc TEXT NOT NULL,metric_name TEXT NOT NULL,value REAL NOT NULL,unit TEXT NOT NULL,tags_json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS chats(id TEXT PRIMARY KEY,created_utc TEXT NOT NULL,updated_utc TEXT NOT NULL,conversation_json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS policies(id TEXT PRIMARY KEY,updated_utc TEXT NOT NULL,policy_json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS plugins(id TEXT PRIMARY KEY,updated_utc TEXT NOT NULL,state_json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS alerts(id TEXT PRIMARY KEY,created_utc TEXT NOT NULL,resolved_utc TEXT NULL,alert_json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS reports(id TEXT PRIMARY KEY,created_utc TEXT NOT NULL,format TEXT NOT NULL,location TEXT NOT NULL,metadata_json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS audit_events(id TEXT PRIMARY KEY,occurred_utc TEXT NOT NULL,actor TEXT NOT NULL,action TEXT NOT NULL,target TEXT NOT NULL,result TEXT NOT NULL,event_json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS schema_migrations(version INTEGER PRIMARY KEY,applied_utc TEXT NOT NULL,description TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_findings_session ON findings(session_id);
            CREATE INDEX IF NOT EXISTS ix_repair_history_created ON repair_history(created_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_diagnosis_reports_generated ON diagnosis_reports(generated_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_health_snapshots_captured ON health_snapshots(captured_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_repair_approvals_requested ON repair_approvals(requested_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_evidence_session ON evidence(scan_session_id);
            CREATE INDEX IF NOT EXISTS ix_timeline_occurred ON timeline_events(occurred_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_metrics_name_captured ON metrics(metric_name,captured_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_alerts_created ON alerts(created_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_audit_occurred ON audit_events(occurred_utc DESC);
            """),
        new(8, "Versioned configuration state", """
            CREATE TABLE IF NOT EXISTS configuration_state(id INTEGER PRIMARY KEY CHECK(id=1),version INTEGER NOT NULL,user_json TEXT NOT NULL,profile_json TEXT NULL,updated_utc TEXT NOT NULL);
            INSERT INTO configuration_state(id,version,user_json,profile_json,updated_utc)
                SELECT 1,1,json,NULL,updated_utc FROM settings WHERE id=1
                ON CONFLICT(id) DO NOTHING;
            """),
        new(9, "Scanner execution provenance", """
            ALTER TABLE scan_sessions ADD COLUMN status TEXT NOT NULL DEFAULT 'Completed';
            ALTER TABLE scan_sessions ADD COLUMN framework_version TEXT NOT NULL DEFAULT '1.0.0';
            CREATE TABLE scanner_executions(id TEXT PRIMARY KEY,session_id TEXT NOT NULL,scanner_id TEXT NOT NULL,display_name TEXT NOT NULL,category TEXT NOT NULL,scanner_version TEXT NOT NULL,status INTEGER NOT NULL,started_utc TEXT NOT NULL,completed_utc TEXT NOT NULL,duration_ms INTEGER NOT NULL,attempts INTEGER NOT NULL,failure_code TEXT NULL,detail TEXT NULL,resource_json TEXT NOT NULL,FOREIGN KEY(session_id) REFERENCES scan_sessions(id) ON DELETE CASCADE);
            CREATE INDEX ix_scanner_executions_session ON scanner_executions(session_id,started_utc);
            CREATE INDEX ix_scanner_executions_scanner ON scanner_executions(scanner_id,completed_utc DESC);
            """),
        new(10, "Driver inventory and conflict analysis", """
            CREATE TABLE driver_analysis_runs(id TEXT PRIMARY KEY,generated_utc TEXT NOT NULL,is_administrator INTEGER NOT NULL CHECK(is_administrator IN (0,1)),inventory_json TEXT NOT NULL,report_json TEXT NOT NULL);
            CREATE INDEX ix_driver_analysis_generated ON driver_analysis_runs(generated_utc DESC);
            """),
        new(11, "Startup and boot analysis", """
            CREATE TABLE boot_analysis_runs(id TEXT PRIMARY KEY,generated_utc TEXT NOT NULL,snapshot_json TEXT NOT NULL,report_json TEXT NOT NULL,rollback_metadata_json TEXT NOT NULL);
            CREATE INDEX ix_boot_analysis_generated ON boot_analysis_runs(generated_utc DESC);
            """),
        new(12, "Windows Update intelligence", """
            CREATE TABLE windows_update_analysis_runs(id TEXT PRIMARY KEY,generated_utc TEXT NOT NULL,snapshot_json TEXT NOT NULL,report_json TEXT NOT NULL,outcomes_json TEXT NOT NULL);
            CREATE INDEX ix_windows_update_analysis_generated ON windows_update_analysis_runs(generated_utc DESC);
            """),
        new(13, "Storage Health Center", """
            CREATE TABLE storage_health_runs(id TEXT PRIMARY KEY,generated_utc TEXT NOT NULL,snapshot_json TEXT NOT NULL,report_json TEXT NOT NULL,exclusions_json TEXT NOT NULL);
            CREATE INDEX ix_storage_health_generated ON storage_health_runs(generated_utc DESC);
            """),
        new(14, "Windows security posture", """
            CREATE TABLE security_posture_runs(id TEXT PRIMARY KEY,generated_utc TEXT NOT NULL,snapshot_json TEXT NOT NULL,report_json TEXT NOT NULL,acknowledgements_json TEXT NOT NULL);
            CREATE INDEX ix_security_posture_generated ON security_posture_runs(generated_utc DESC);
            """),
        new(15, "Grounded AI chat conversations", """
            CREATE TABLE chat_conversations(id TEXT PRIMARY KEY,updated_utc TEXT NOT NULL,is_deleted INTEGER NOT NULL CHECK(is_deleted IN(0,1)),exported INTEGER NOT NULL CHECK(exported IN(0,1)),conversation_json TEXT NOT NULL);
            CREATE INDEX ix_chat_conversations_updated ON chat_conversations(updated_utc DESC);
            """),
        new(16, "Network diagnostic history", """
            CREATE TABLE network_health_runs(id TEXT PRIMARY KEY,generated_utc TEXT NOT NULL,snapshot_json TEXT NOT NULL,tests_json TEXT NOT NULL,report_json TEXT NOT NULL);
            CREATE INDEX ix_network_health_generated ON network_health_runs(generated_utc DESC);
            """)
,
        new(17, "Evidence aggregation graph", """
            CREATE TABLE evidence_graph_runs(id TEXT PRIMARY KEY,generated_utc TEXT NOT NULL,schema_version TEXT NOT NULL,strategy_version TEXT NOT NULL,retain_until_utc TEXT NOT NULL);
            CREATE TABLE evidence_graph_nodes(run_id TEXT NOT NULL,node_id TEXT NOT NULL,observed_utc TEXT NOT NULL,domain TEXT NOT NULL,code TEXT NOT NULL,node_json TEXT NOT NULL,PRIMARY KEY(run_id,node_id),FOREIGN KEY(run_id) REFERENCES evidence_graph_runs(id) ON DELETE CASCADE);
            CREATE TABLE evidence_graph_edges(run_id TEXT NOT NULL,edge_id TEXT NOT NULL,from_node_id TEXT NOT NULL,to_node_id TEXT NOT NULL,kind TEXT NOT NULL,confidence REAL NOT NULL,edge_json TEXT NOT NULL,PRIMARY KEY(run_id,edge_id),FOREIGN KEY(run_id) REFERENCES evidence_graph_runs(id) ON DELETE CASCADE);
            CREATE INDEX ix_evidence_graph_runs_generated ON evidence_graph_runs(generated_utc DESC);
            CREATE INDEX ix_evidence_graph_nodes_domain_time ON evidence_graph_nodes(domain,observed_utc DESC);
            CREATE INDEX ix_evidence_graph_edges_nodes ON evidence_graph_edges(from_node_id,to_node_id);
            """),
        new(18, "Repair recommendation rankings", """
            CREATE TABLE repair_recommendation_runs(id TEXT PRIMARY KEY,generated_utc TEXT NOT NULL,ranking_version TEXT NOT NULL,run_json TEXT NOT NULL);
            CREATE INDEX ix_repair_recommendation_generated ON repair_recommendation_runs(generated_utc DESC);
            """),
        new(19, "Predictive health history", """
            CREATE TABLE predictive_health_runs(id TEXT PRIMARY KEY,generated_utc TEXT NOT NULL,model_version TEXT NOT NULL,report_json TEXT NOT NULL);
            CREATE INDEX ix_predictive_health_generated ON predictive_health_runs(generated_utc DESC);
            """),
        new(20, "Live monitoring sessions and bounded samples", """
            CREATE TABLE IF NOT EXISTS monitoring_sessions(id TEXT PRIMARY KEY,started_utc TEXT NOT NULL,ended_utc TEXT NULL,state INTEGER NOT NULL,options_json TEXT NOT NULL,stop_reason TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS monitoring_samples(id TEXT PRIMARY KEY,session_id TEXT NOT NULL,signal_id TEXT NOT NULL,captured_utc TEXT NOT NULL,sample_json TEXT NOT NULL,FOREIGN KEY(session_id) REFERENCES monitoring_sessions(id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS monitoring_gaps(id TEXT PRIMARY KEY,session_id TEXT NOT NULL,signal_id TEXT NOT NULL,captured_utc TEXT NOT NULL,sample_json TEXT NOT NULL,FOREIGN KEY(session_id) REFERENCES monitoring_sessions(id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS monitoring_collector_failures(id TEXT PRIMARY KEY,session_id TEXT NOT NULL,signal_id TEXT NOT NULL,captured_utc TEXT NOT NULL,sample_json TEXT NOT NULL,FOREIGN KEY(session_id) REFERENCES monitoring_sessions(id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS monitoring_retention(id INTEGER PRIMARY KEY CHECK(id=1),evaluated_utc TEXT NOT NULL,state_json TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_monitoring_samples_signal_time ON monitoring_samples(signal_id,captured_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_monitoring_gaps_session_time ON monitoring_gaps(session_id,captured_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_monitoring_failures_session_time ON monitoring_collector_failures(session_id,captured_utc DESC);
            """),
        new(21, "Versioned reliability timeline projection", """
            CREATE TABLE IF NOT EXISTS timeline_incidents(id TEXT PRIMARY KEY,started_utc TEXT NOT NULL,ended_utc TEXT NOT NULL,incident_json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS timeline_projection_state(id INTEGER PRIMARY KEY CHECK(id=1),projection_version TEXT NOT NULL,grouping_version TEXT NOT NULL,generated_utc TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_timeline_incidents_started ON timeline_incidents(started_utc DESC,id);
            CREATE INDEX IF NOT EXISTS ix_timeline_category_occurred ON timeline_events(category,occurred_utc DESC,id);
            """),
        new(22, "Performance samples, rollups, and retention", """
            CREATE TABLE IF NOT EXISTS performance_samples(id TEXT PRIMARY KEY,metric_kind TEXT NOT NULL,captured_utc TEXT NOT NULL,value REAL NULL,unit TEXT NOT NULL,quality INTEGER NOT NULL,source TEXT NOT NULL,detail TEXT NULL);
            CREATE TABLE IF NOT EXISTS performance_rollups(id TEXT PRIMARY KEY,metric_kind TEXT NOT NULL,resolution INTEGER NOT NULL,period_start_utc TEXT NOT NULL,period_end_utc TEXT NOT NULL,minimum REAL NULL,maximum REAL NULL,average REAL NULL,p95 REAL NULL,sample_count INTEGER NOT NULL,coverage REAL NOT NULL,unit TEXT NOT NULL,quality INTEGER NOT NULL,aggregation_version TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS performance_retention_jobs(id TEXT PRIMARY KEY,executed_utc TEXT NOT NULL,raw_retain_after_utc TEXT NOT NULL,rollup_retain_after_utc TEXT NOT NULL,deleted_samples INTEGER NOT NULL,deleted_rollups INTEGER NOT NULL,policy_version TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_performance_samples_kind_time ON performance_samples(metric_kind,captured_utc,id);
            CREATE INDEX IF NOT EXISTS ix_performance_rollups_query ON performance_rollups(metric_kind,resolution,period_start_utc,id);
            """),
        new(23, "Digital twin snapshots and diffs", """
            CREATE TABLE IF NOT EXISTS digital_twin_snapshots(id TEXT PRIMARY KEY,captured_utc TEXT NOT NULL,purpose INTEGER NOT NULL,is_pinned INTEGER NOT NULL,repair_transaction_id TEXT NULL,related_snapshot_id TEXT NULL,integrity_sha256 TEXT NOT NULL,metadata_json TEXT NOT NULL,compressed_payload BLOB NOT NULL);
            CREATE TABLE IF NOT EXISTS digital_twin_diffs(id TEXT PRIMARY KEY,before_id TEXT NOT NULL,after_id TEXT NOT NULL,compared_utc TEXT NOT NULL,diff_json TEXT NOT NULL,FOREIGN KEY(before_id) REFERENCES digital_twin_snapshots(id),FOREIGN KEY(after_id) REFERENCES digital_twin_snapshots(id));
            CREATE TABLE IF NOT EXISTS digital_twin_retention_jobs(id TEXT PRIMARY KEY,executed_utc TEXT NOT NULL,result_json TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_digital_twin_captured ON digital_twin_snapshots(captured_utc DESC,is_pinned);
            """),
        new(24, "Scheduled scan execution history", """
            CREATE TABLE IF NOT EXISTS scheduled_scan_history(id TEXT PRIMARY KEY,evaluated_utc TEXT NOT NULL,outcome INTEGER NOT NULL,history_json TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_scheduled_scan_history_time ON scheduled_scan_history(evaluated_utc DESC,id DESC);
            """)    ];
}
