using System.Data;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Infrastructure.Data;

namespace RestaurantPOS.Infrastructure.Services;

/// <summary>
/// Production-grade SQL Server backup / restore / smart-import service.
///
/// Key design notes
/// ─────────────────
/// • Backups live in C:\ProgramData\RestaurantPOS\Backups. This folder is
///   granted to the SQL Server service account on first use, so BACKUP DATABASE
///   writes directly there (no more "Operating system error 5").
/// • COMPRESSION is auto-detected per edition (Express does not support it).
/// • Backup rotation keeps the last N files (default 7).
/// • After every backup, all currently-mounted USB drives receive a copy.
/// • SmartImport restores the .bak to a staging DB and INSERT ... NOT EXISTS
///   from staging to live for every matching table (no destructive overwrites).
/// • All public methods swallow expected exceptions and return result objects.
/// </summary>
public class SqlServerMaintenanceService : IDatabaseMaintenanceService
{
    private readonly PosDbContext _db;
    private readonly string _backupPath;
    private readonly string _logPath;

    private bool? _compressionSupported; // null = not yet detected
    private bool _aclGranted;            // one-time per session

    public SqlServerMaintenanceService(PosDbContext db)
    {
        _db = db;
        _backupPath = DatabaseConfig.GetBackupPath(); // C:\ProgramData\RestaurantPOS\Backups
        _logPath = Path.Combine(_backupPath, "backup.log");
    }

    public string BackupExtension => ".bak";
    public string FileFilter => "SQL Server Backup|*.bak";

    // ═════════════════════════════════════════════════════════
    //  DATABASE SIZE + FLUSH
    // ═════════════════════════════════════════════════════════

    public async Task<string> GetDatabaseSizeAsync()
    {
        try
        {
            await using var conn = new SqlConnection(DatabaseConfig.GetConnectionString());
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT CAST(SUM(size) * 8.0 / 1024 AS DECIMAL(10,1))
                FROM sys.database_files";
            var result = await cmd.ExecuteScalarAsync();
            if (result != null && result != DBNull.Value)
                return $"{result} MB";
        }
        catch { /* ignore */ }
        return "N/A";
    }

    public Task FlushAsync() => Task.CompletedTask;

    // ═════════════════════════════════════════════════════════
    //  LEGACY API — kept for existing Settings tab commands
    // ═════════════════════════════════════════════════════════

    public async Task<string> BackupAsync()
    {
        var r = await BackupWithRotationAsync();
        if (!r.Success) throw new InvalidOperationException(r.Error ?? "Backup failed.");
        return r.BackupFilePath!;
    }

    public async Task RestoreAsync(string backupFilePath)
    {
        var settings = DatabaseConfig.GetSettings();
        var dbName = settings.Database;

        // Safety backup first
        try { await BackupWithRotationAsync(); } catch { /* best effort */ }

        // Make sure SQL can read the .bak
        var sourceForRestore = await EnsureReadableBySqlAsync(backupFilePath);

        var sql = $@"
            ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            RESTORE DATABASE [{dbName}] FROM DISK = N'{Escape(sourceForRestore)}' WITH REPLACE;
            ALTER DATABASE [{dbName}] SET MULTI_USER;";

        await using var conn = new SqlConnection(ReplaceInitialCatalog(DatabaseConfig.GetConnectionString(), "master"));
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 600;
        await cmd.ExecuteNonQueryAsync();

        if (!string.Equals(sourceForRestore, backupFilePath, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(sourceForRestore); } catch { }
        }

        Log($"Restore complete from: {backupFilePath}");
    }

    public async Task ExportAsync(string destinationPath)
    {
        await EnsureBackupFolderReadyAsync();

        var settings = DatabaseConfig.GetSettings();
        var dbName = settings.Database;

        var destDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            Directory.CreateDirectory(destDir);

        try
        {
            await ExecuteBackupToPathAsync(dbName, destinationPath);
            Log($"Export OK: {destinationPath}");
            return;
        }
        catch (SqlException ex) when (IsAccessDenied(ex))
        {
            // Fall back via SQL's default folder + OPENROWSET stream back
            var sqlDir = await GetSqlServerDefaultBackupDirectoryAsync();
            if (string.IsNullOrWhiteSpace(sqlDir))
                throw new InvalidOperationException(
                    $"Export failed: SQL Server cannot write to '{destinationPath}' (Access denied).", ex);

            var intermediate = Path.Combine(sqlDir, Path.GetFileName(destinationPath));
            await ExecuteBackupToPathAsync(dbName, intermediate);

            try
            {
                await StreamFileFromSqlAsync(intermediate, destinationPath);
                await TryDeleteFileViaSqlAsync(intermediate);
                Log($"Export OK (via intermediate): {destinationPath}");
            }
            catch (Exception moveEx)
            {
                throw new InvalidOperationException(
                    $"The backup was created at '{intermediate}' but could not be copied to " +
                    $"'{destinationPath}'. Reason: {moveEx.Message}", moveEx);
            }
        }
    }

    public Task<string> ImportAsync(string sourceFilePath)
    {
        if (!Directory.Exists(_backupPath)) Directory.CreateDirectory(_backupPath);

        var destFile = Path.Combine(_backupPath, Path.GetFileName(sourceFilePath));

        if (File.Exists(destFile) &&
            !string.Equals(Path.GetFullPath(sourceFilePath), Path.GetFullPath(destFile), StringComparison.OrdinalIgnoreCase))
        {
            var name = Path.GetFileNameWithoutExtension(destFile);
            var ext = Path.GetExtension(destFile);
            destFile = Path.Combine(_backupPath, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}{ext}");
        }

        File.Copy(sourceFilePath, destFile, overwrite: true);
        Log($"Imported to backup folder: {destFile}");
        return Task.FromResult(destFile);
    }

    // ═════════════════════════════════════════════════════════
    //  NEW: FULL PRODUCTION BACKUP (rotation + USB copy + log)
    // ═════════════════════════════════════════════════════════

    public async Task<BackupResult> BackupWithRotationAsync(int keepLast = 7)
    {
        try
        {
            await EnsureBackupFolderReadyAsync();
            var settings = DatabaseConfig.GetSettings();
            var dbName = settings.Database;

            var fileName = $"backup_{DateTime.Now:yyyyMMdd_HHmm}.bak";
            var backupFile = Path.Combine(_backupPath, fileName);

            // 1) Create the backup (with fallback path if SQL can't write directly)
            var finalPath = await BackupWithAccessFallbackAsync(dbName, backupFile);

            var size = 0L;
            try { size = new FileInfo(finalPath).Length; } catch { }

            Log($"Backup created: {finalPath} ({FormatSize(size)})");

            // 2) Rotate: keep only the latest N backups in the folder
            var rotated = RotateBackups(keepLast);
            if (rotated > 0) Log($"Rotation: deleted {rotated} old backup file(s).");

            // 3) USB auto-copy
            var (usbMsg, usbCopies) = await CopyToAllUsbDrivesAsync(finalPath);
            if (!string.IsNullOrEmpty(usbMsg)) Log($"USB copy: {usbMsg}");

            return new BackupResult
            {
                Success = true,
                BackupFilePath = finalPath,
                FileSizeBytes = size,
                RotatedCount = rotated,
                UsbMessage = usbMsg,
                UsbCopies = usbCopies,
                Message = $"Backup successful. Saved to: {finalPath}" +
                          (usbCopies.Count > 0 ? $"\n{usbCopies.Count} USB copy(ies) made." : ""),
            };
        }
        catch (SqlException sqlEx)
        {
            Log($"ERROR (SQL): {sqlEx.Message}");
            return new BackupResult
            {
                Success = false,
                Error = sqlEx.Message,
                Message = "Backup failed (SQL Server error). See log for details.",
            };
        }
        catch (IOException ioEx)
        {
            Log($"ERROR (IO): {ioEx.Message}");
            return new BackupResult
            {
                Success = false,
                Error = ioEx.Message,
                Message = "Backup failed (file system error). See log for details.",
            };
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.GetType().Name}: {ex.Message}");
            return new BackupResult
            {
                Success = false,
                Error = ex.Message,
                Message = "Backup failed. See log for details.",
            };
        }
    }

    /// <summary>Delete older backups, keeping the N newest. Returns number deleted.</summary>
    private int RotateBackups(int keepLast)
    {
        if (keepLast <= 0) return 0;
        var deleted = 0;
        try
        {
            if (!Directory.Exists(_backupPath)) return 0;
            var old = Directory.GetFiles(_backupPath, "*.bak")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .Skip(keepLast)
                .ToList();
            foreach (var f in old)
            {
                try { f.Delete(); deleted++; }
                catch (Exception ex) { Log($"Rotation: could not delete {f.Name} — {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            Log($"Rotation failed: {ex.Message}");
        }
        return deleted;
    }

    /// <summary>Do the BACKUP; if access denied, write to SQL's folder and stream bytes back.</summary>
    private async Task<string> BackupWithAccessFallbackAsync(string dbName, string preferredPath)
    {
        // Try direct write
        try
        {
            await ExecuteBackupToPathAsync(dbName, preferredPath);
            return preferredPath;
        }
        catch (SqlException ex) when (IsAccessDenied(ex))
        {
            Log($"Access denied on direct BACKUP to '{preferredPath}' — trying SQL's default backup dir.");
        }

        // Fall back via SQL's default backup folder + OPENROWSET stream back
        var sqlDir = await GetSqlServerDefaultBackupDirectoryAsync();
        if (string.IsNullOrWhiteSpace(sqlDir))
            throw new InvalidOperationException(
                $"Backup failed: SQL Server cannot write to '{preferredPath}' and its " +
                "default backup directory could not be determined. " +
                "Grant the SQL Server service account modify rights on the backup folder, " +
                "or run the application once as Administrator.");

        var fileName = Path.GetFileName(preferredPath);
        var intermediate = Path.Combine(sqlDir, fileName);
        await ExecuteBackupToPathAsync(dbName, intermediate);

        try
        {
            await StreamFileFromSqlAsync(intermediate, preferredPath);
            await TryDeleteFileViaSqlAsync(intermediate);
            return preferredPath;
        }
        catch
        {
            // Couldn't copy — return the SQL-side path so at least the backup is usable
            Log($"WARNING: Backup created at '{intermediate}' but could not be copied to '{preferredPath}'.");
            return intermediate;
        }
    }

    // ═════════════════════════════════════════════════════════
    //  NEW: USB DETECTION + COPY
    // ═════════════════════════════════════════════════════════

    public IReadOnlyList<UsbDriveInfo> GetUsbDrives()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Removable)
                .Select(d => new UsbDriveInfo
                {
                    RootDirectory = d.RootDirectory.FullName,
                    VolumeLabel = SafeLabel(d),
                    AvailableFreeBytes = SafeFree(d),
                    TotalBytes = SafeTotal(d),
                })
                .ToList();
        }
        catch { return Array.Empty<UsbDriveInfo>(); }
    }

    private async Task<(string message, List<string> copies)> CopyToAllUsbDrivesAsync(string backupFile)
    {
        var copies = new List<string>();
        var drives = GetUsbDrives();
        if (drives.Count == 0)
            return ("No USB drive detected.", copies);

        var errors = new List<string>();
        foreach (var d in drives)
        {
            try
            {
                var destDir = Path.Combine(d.RootDirectory, "RestaurantPOS_Backups");
                Directory.CreateDirectory(destDir);
                var destFile = Path.Combine(destDir, Path.GetFileName(backupFile));
                await Task.Run(() => File.Copy(backupFile, destFile, overwrite: true));
                copies.Add(destFile);
            }
            catch (Exception ex)
            {
                errors.Add($"{d.Display}: {ex.Message}");
            }
        }

        var msg = copies.Count switch
        {
            0 => $"USB copy failed. {string.Join("; ", errors)}",
            _ when errors.Count == 0 => $"Copied to {copies.Count} USB drive(s).",
            _ => $"Copied to {copies.Count} USB drive(s); {errors.Count} failed: {string.Join("; ", errors)}",
        };
        return (msg, copies);
    }

    // ═════════════════════════════════════════════════════════
    //  NEW: SMART IMPORT (merge only missing rows)
    // ═════════════════════════════════════════════════════════

    public async Task<SmartImportResult> SmartImportAsync(string bakFilePath)
    {
        var details = new List<string>();
        var stagingDb = $"RestaurantPOS_Import_{DateTime.Now:yyyyMMddHHmmss}";
        var liveDb = DatabaseConfig.GetSettings().Database;
        long totalInserted = 0;
        int merged = 0, skipped = 0;
        int comparedTables = 0;

        try
        {
            // Ensure SQL can read the .bak
            var sourceBak = await EnsureReadableBySqlAsync(bakFilePath);
            details.Add($"Source: {bakFilePath}");
            Log($"SmartImport start — staging='{stagingDb}', source='{sourceBak}'");

            // Step 1: Restore .bak into staging DB
            await RestoreBakToNewDatabaseAsync(sourceBak, stagingDb);
            details.Add($"Restored to staging database: {stagingDb}");

            // Step 2: Find tables that exist in BOTH databases (dbo schema)
            var commonTables = await GetCommonTablesAsync(liveDb, stagingDb);
            comparedTables = commonTables.Count;
            details.Add($"Common tables: {comparedTables}");

            // Step 3: Disable FK constraints on live DB (avoid FK errors during merge)
            await ExecOnDbAsync(liveDb, "EXEC sp_MSforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL'");

            // Step 4: For each table, insert rows that don't already exist
            foreach (var table in commonTables)
            {
                try
                {
                    var inserted = await MergeTableAsync(liveDb, stagingDb, table);
                    if (inserted >= 0)
                    {
                        merged++;
                        totalInserted += inserted;
                        if (inserted > 0) details.Add($"  {table}: +{inserted} row(s)");
                    }
                    else
                    {
                        skipped++;
                        details.Add($"  {table}: skipped (no primary key and column shape differs)");
                    }
                }
                catch (Exception ex)
                {
                    skipped++;
                    details.Add($"  {table}: ERROR — {ex.Message}");
                    Log($"SmartImport table '{table}' error: {ex.Message}");
                }
            }

            // Step 5: Re-enable constraints
            try
            {
                await ExecOnDbAsync(liveDb, "EXEC sp_MSforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL'");
            }
            catch (Exception ex)
            {
                details.Add($"Warning: re-enable FK constraints: {ex.Message}");
            }

            // Cleanup intermediate .bak copy (if we created one in SQL's folder)
            if (!string.Equals(sourceBak, bakFilePath, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(sourceBak); } catch { }
            }

            Log($"SmartImport OK — tables={comparedTables} merged={merged} skipped={skipped} rows+={totalInserted}");

            return new SmartImportResult
            {
                Success = true,
                TablesCompared = comparedTables,
                TablesMerged = merged,
                TablesSkipped = skipped,
                RowsInserted = totalInserted,
                Details = details,
                Message = $"Smart import complete. {merged}/{comparedTables} tables merged, " +
                          $"{totalInserted} new row(s) inserted.",
            };
        }
        catch (Exception ex)
        {
            Log($"SmartImport FAILED: {ex.Message}");
            details.Add($"FATAL: {ex.Message}");
            return new SmartImportResult
            {
                Success = false,
                TablesCompared = comparedTables,
                TablesMerged = merged,
                TablesSkipped = skipped,
                RowsInserted = totalInserted,
                Details = details,
                Error = ex.Message,
                Message = "Smart import failed. See details.",
            };
        }
        finally
        {
            // Always drop the staging database
            try
            {
                await ExecOnDbAsync("master",
                    $"IF DB_ID(N'{stagingDb}') IS NOT NULL " +
                    $"BEGIN ALTER DATABASE [{stagingDb}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                    $"DROP DATABASE [{stagingDb}]; END");
                Log($"Dropped staging DB: {stagingDb}");
            }
            catch (Exception ex)
            {
                Log($"Staging cleanup WARNING: {ex.Message}");
            }
        }
    }

    // ─── Smart-import helpers ─────────────────────────────────

    private async Task RestoreBakToNewDatabaseAsync(string bakPath, string newDbName)
    {
        // 1) Read logical file names from the .bak via FILELISTONLY
        var logicalFiles = new List<(string LogicalName, string Type)>();
        await using (var conn = new SqlConnection(ReplaceInitialCatalog(DatabaseConfig.GetConnectionString(), "master")))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"RESTORE FILELISTONLY FROM DISK = N'{Escape(bakPath)}'";
            cmd.CommandTimeout = 120;
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                logicalFiles.Add((
                    LogicalName: rdr.GetString(rdr.GetOrdinal("LogicalName")),
                    Type:        rdr.GetString(rdr.GetOrdinal("Type"))));
            }
        }

        if (logicalFiles.Count == 0)
            throw new InvalidOperationException("Backup file has no logical files (invalid .bak?).");

        // 2) Where does SQL Server put data files? Fall back to a known-good path.
        var dataDir = await GetSqlServerDefaultDataDirectoryAsync() ?? Path.GetTempPath();

        // 3) Build MOVE clauses so files land in dataDir with staging db name
        var moves = new StringBuilder();
        int dataIdx = 0, logIdx = 0;
        foreach (var lf in logicalFiles)
        {
            var ext = lf.Type.Equals("L", StringComparison.OrdinalIgnoreCase) ? "_log.ldf" : ".mdf";
            var suffix = lf.Type.Equals("L", StringComparison.OrdinalIgnoreCase)
                ? (logIdx++ == 0 ? "" : "_" + logIdx)
                : (dataIdx++ == 0 ? "" : "_" + dataIdx);
            var physical = Path.Combine(dataDir, $"{newDbName}{suffix}{ext}");
            if (moves.Length > 0) moves.Append(", ");
            moves.Append($"MOVE N'{Escape(lf.LogicalName)}' TO N'{Escape(physical)}'");
        }

        var sql = $"RESTORE DATABASE [{newDbName}] FROM DISK = N'{Escape(bakPath)}' " +
                  $"WITH {moves}, REPLACE, STATS = 10";

        await using (var conn2 = new SqlConnection(ReplaceInitialCatalog(DatabaseConfig.GetConnectionString(), "master")))
        {
            await conn2.OpenAsync();
            await using var cmd2 = conn2.CreateCommand();
            cmd2.CommandText = sql;
            cmd2.CommandTimeout = 900;
            await cmd2.ExecuteNonQueryAsync();
        }
    }

    private async Task<List<string>> GetCommonTablesAsync(string liveDb, string stagingDb)
    {
        var live = await GetUserTablesAsync(liveDb);
        var staging = await GetUserTablesAsync(stagingDb);
        return live.Intersect(staging, StringComparer.OrdinalIgnoreCase)
                   .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                   .ToList();
    }

    private async Task<List<string>> GetUserTablesAsync(string dbName)
    {
        var list = new List<string>();
        await using var conn = new SqlConnection(ReplaceInitialCatalog(DatabaseConfig.GetConnectionString(), dbName));
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.name
            FROM sys.tables t
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = 'dbo' AND t.is_ms_shipped = 0
            ORDER BY t.name";
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync()) list.Add(rdr.GetString(0));
        return list;
    }

    /// <summary>
    /// Insert rows from staging.[table] into live.[table] that are not already present.
    /// Matches by primary key if one exists; otherwise falls back to all non-computed columns.
    /// Returns rows inserted, or -1 if the table was skipped.
    /// </summary>
    private async Task<int> MergeTableAsync(string liveDb, string stagingDb, string table)
    {
        // Load the live table's column metadata
        var liveCols = await GetTableColumnsAsync(liveDb, table);
        var stagingCols = await GetTableColumnsAsync(stagingDb, table);

        // Only use columns present in BOTH (schema drift safety)
        var commonCols = liveCols
            .Where(lc => !lc.IsComputed && stagingCols.Any(sc =>
                string.Equals(sc.Name, lc.Name, StringComparison.OrdinalIgnoreCase) && !sc.IsComputed))
            .ToList();

        if (commonCols.Count == 0) return -1;

        var hasIdentity = commonCols.Any(c => c.IsIdentity);
        var pkCols = commonCols.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();

        // Build column list
        var colList = string.Join(", ", commonCols.Select(c => $"[{c.Name}]"));

        // WHERE NOT EXISTS predicate
        string whereNotExists;
        if (pkCols.Count > 0)
        {
            whereNotExists = string.Join(" AND ",
                pkCols.Select(c => $"live.[{c}] = staging.[{c}]"));
        }
        else
        {
            // No PK — compare on every common column (expensive but safe)
            whereNotExists = string.Join(" AND ",
                commonCols.Select(c => $"((live.[{c.Name}] = staging.[{c.Name}]) " +
                                       $"OR (live.[{c.Name}] IS NULL AND staging.[{c.Name}] IS NULL))"));
        }

        var sql = new StringBuilder();
        if (hasIdentity) sql.AppendLine($"SET IDENTITY_INSERT [{liveDb}].dbo.[{table}] ON;");
        sql.AppendLine($@"
            INSERT INTO [{liveDb}].dbo.[{table}] ({colList})
            SELECT {colList}
            FROM [{stagingDb}].dbo.[{table}] AS staging
            WHERE NOT EXISTS (
                SELECT 1 FROM [{liveDb}].dbo.[{table}] AS live
                WHERE {whereNotExists}
            );");
        if (hasIdentity) sql.AppendLine($"SET IDENTITY_INSERT [{liveDb}].dbo.[{table}] OFF;");

        // Execute against master DB (so 3-part names resolve)
        await using var conn = new SqlConnection(ReplaceInitialCatalog(DatabaseConfig.GetConnectionString(), "master"));
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql.ToString();
        cmd.CommandTimeout = 900;
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows < 0 ? 0 : rows;
    }

    private async Task<List<ColumnInfo>> GetTableColumnsAsync(string dbName, string table)
    {
        var cols = new List<ColumnInfo>();
        await using var conn = new SqlConnection(ReplaceInitialCatalog(DatabaseConfig.GetConnectionString(), dbName));
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                c.name,
                c.is_identity,
                c.is_computed,
                CAST(ISNULL(ic.is_pk, 0) AS bit) AS is_primary_key
            FROM sys.columns c
            LEFT JOIN (
                SELECT ic.column_id, ic.object_id, 1 AS is_pk
                FROM sys.index_columns ic
                INNER JOIN sys.indexes i
                    ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                WHERE i.is_primary_key = 1
            ) ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE c.object_id = OBJECT_ID(@tbl)
            ORDER BY c.column_id";
        var p = cmd.CreateParameter();
        p.ParameterName = "@tbl";
        p.Value = $"dbo.{table}";
        cmd.Parameters.Add(p);

        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            cols.Add(new ColumnInfo
            {
                Name = rdr.GetString(0),
                IsIdentity = rdr.GetBoolean(1),
                IsComputed = rdr.GetBoolean(2),
                IsPrimaryKey = rdr.GetBoolean(3),
            });
        }
        return cols;
    }

    private sealed class ColumnInfo
    {
        public string Name { get; init; } = "";
        public bool IsIdentity { get; init; }
        public bool IsComputed { get; init; }
        public bool IsPrimaryKey { get; init; }
    }

    // ═════════════════════════════════════════════════════════
    //  BACKUP EXECUTION + EDITION DETECTION
    // ═════════════════════════════════════════════════════════

    private async Task ExecuteBackupToPathAsync(string dbName, string path)
    {
        await EnsureCompressionCapabilityDetectedAsync();

        var withClause = _compressionSupported == true
            ? "WITH FORMAT, INIT, NAME = N'POS Backup', COMPRESSION, STATS = 10"
            : "WITH FORMAT, INIT, NAME = N'POS Backup', STATS = 10";

        var sql = $"BACKUP DATABASE [{dbName}] TO DISK = N'{Escape(path)}' {withClause}";

        await using var conn = new SqlConnection(DatabaseConfig.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 600;

        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (SqlException ex) when (IsCompressionUnsupported(ex))
        {
            _compressionSupported = false;
            cmd.CommandText = $"BACKUP DATABASE [{dbName}] TO DISK = N'{Escape(path)}' " +
                              "WITH FORMAT, INIT, NAME = N'POS Backup', STATS = 10";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task EnsureCompressionCapabilityDetectedAsync()
    {
        if (_compressionSupported.HasValue) return;
        try
        {
            await using var conn = new SqlConnection(DatabaseConfig.GetConnectionString());
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    CAST(SERVERPROPERTY('EngineEdition') AS INT),
                    CAST(SERVERPROPERTY('ProductMajorVersion') AS INT),
                    CAST(SERVERPROPERTY('Edition') AS NVARCHAR(200))";
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var engine = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                var major  = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                var edName = r.IsDBNull(2) ? "" : r.GetString(2);

                if (engine == 4 || engine == 1 ||
                    edName.Contains("Express", StringComparison.OrdinalIgnoreCase))
                { _compressionSupported = false; return; }

                if (engine == 3) { _compressionSupported = true; return; }
                if (engine == 2) { _compressionSupported = major >= 13; return; }
                _compressionSupported = true;
                return;
            }
        }
        catch { }
        _compressionSupported = false;
    }

    // ═════════════════════════════════════════════════════════
    //  ACL: grant SQL Server service account modify on backup dir
    // ═════════════════════════════════════════════════════════

    private async Task EnsureBackupFolderReadyAsync()
    {
        if (!Directory.Exists(_backupPath)) Directory.CreateDirectory(_backupPath);

        if (_aclGranted) return;

        try
        {
            var svcAccount = await GetSqlServiceAccountAsync();
            if (!string.IsNullOrWhiteSpace(svcAccount))
            {
                GrantFolderModify(_backupPath, svcAccount!);
                Log($"Granted '{svcAccount}' modify rights on '{_backupPath}'.");
            }
            _aclGranted = true;
        }
        catch (Exception ex)
        {
            Log($"WARNING: ACL grant skipped — {ex.Message}");
            _aclGranted = true; // Don't keep retrying every backup
        }
    }

    private static void GrantFolderModify(string folderPath, string account)
    {
        if (!OperatingSystem.IsWindows()) return;
        var di = new DirectoryInfo(folderPath);
        var security = di.GetAccessControl(AccessControlSections.Access);
        var rule = new FileSystemAccessRule(
            new NTAccount(account),
            FileSystemRights.Modify | FileSystemRights.Synchronize,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow);
        security.ModifyAccessRule(AccessControlModification.Add, rule, out _);
        di.SetAccessControl(security);
    }

    private async Task<string?> GetSqlServiceAccountAsync()
    {
        // Prefer DMV (SQL 2008 R2+)
        try
        {
            await using var conn = new SqlConnection(DatabaseConfig.GetConnectionString());
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT TOP 1 service_account
                FROM sys.dm_server_services
                WHERE servicename LIKE 'SQL Server (%'";
            var r = await cmd.ExecuteScalarAsync();
            var s = r?.ToString();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        catch { }
        return null;
    }

    // ═════════════════════════════════════════════════════════
    //  FILE TRANSFER VIA SQL (OPENROWSET stream-back)
    // ═════════════════════════════════════════════════════════

    private async Task StreamFileFromSqlAsync(string sqlSidePath, string localDestPath)
    {
        await using var conn = new SqlConnection(DatabaseConfig.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT BulkColumn FROM OPENROWSET(BULK N'{Escape(sqlSidePath)}', SINGLE_BLOB) AS x";
        cmd.CommandTimeout = 600;
        var result = await cmd.ExecuteScalarAsync();
        if (result is not byte[] bytes)
            throw new InvalidOperationException("SQL Server returned no data for the backup file.");

        var dir = Path.GetDirectoryName(localDestPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(localDestPath, bytes);
    }

    private async Task TryDeleteFileViaSqlAsync(string sqlSidePath)
    {
        try
        {
            await using var conn = new SqlConnection(DatabaseConfig.GetConnectionString());
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            // xp_delete_files (plural) exists on SQL 2019+; ignore if unavailable
            cmd.CommandText = $@"
                BEGIN TRY
                    EXEC master.sys.xp_delete_files N'{Escape(sqlSidePath)}';
                END TRY
                BEGIN CATCH END CATCH";
            cmd.CommandTimeout = 30;
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* best effort */ }
    }

    /// <summary>
    /// If <paramref name="userPath"/> is a location SQL cannot read, copy it into
    /// SQL's default backup directory and return the new path.
    /// </summary>
    private async Task<string> EnsureReadableBySqlAsync(string userPath)
    {
        var sqlDir = await GetSqlServerDefaultBackupDirectoryAsync();
        if (string.IsNullOrWhiteSpace(sqlDir)) return userPath;

        try
        {
            var full = Path.GetFullPath(userPath);
            var sqlFull = Path.GetFullPath(sqlDir);
            if (full.StartsWith(sqlFull, StringComparison.OrdinalIgnoreCase)) return userPath;
        }
        catch { }

        try
        {
            if (!Directory.Exists(sqlDir)) Directory.CreateDirectory(sqlDir);
            var dest = Path.Combine(sqlDir, Path.GetFileName(userPath));
            File.Copy(userPath, dest, overwrite: true);
            return dest;
        }
        catch
        {
            return userPath;
        }
    }

    // ═════════════════════════════════════════════════════════
    //  Server-default paths
    // ═════════════════════════════════════════════════════════

    private async Task<string?> GetSqlServerDefaultBackupDirectoryAsync()
    {
        try
        {
            await using var conn = new SqlConnection(DatabaseConfig.GetConnectionString());
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DECLARE @p NVARCHAR(4000) = CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS NVARCHAR(4000));
                IF @p IS NULL OR @p = ''
                BEGIN
                    EXEC master.dbo.xp_instance_regread
                        N'HKEY_LOCAL_MACHINE',
                        N'Software\Microsoft\MSSQLServer\MSSQLServer',
                        N'BackupDirectory',
                        @p OUTPUT,
                        N'no_output';
                END
                SELECT @p;";
            var r = await cmd.ExecuteScalarAsync();
            var s = r?.ToString();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        catch { }
        return null;
    }

    private async Task<string?> GetSqlServerDefaultDataDirectoryAsync()
    {
        try
        {
            await using var conn = new SqlConnection(DatabaseConfig.GetConnectionString());
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(4000));";
            var r = await cmd.ExecuteScalarAsync();
            var s = r?.ToString();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        catch { }
        return null;
    }

    // ═════════════════════════════════════════════════════════
    //  Low-level SQL helpers
    // ═════════════════════════════════════════════════════════

    private async Task ExecOnDbAsync(string dbName, string sql)
    {
        await using var conn = new SqlConnection(ReplaceInitialCatalog(DatabaseConfig.GetConnectionString(), dbName));
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 900;
        await cmd.ExecuteNonQueryAsync();
    }

    private static string ReplaceInitialCatalog(string connectionString, string newCatalog)
    {
        var b = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = newCatalog };
        return b.ToString();
    }

    private static string Escape(string s) => s.Replace("'", "''");

    private static bool IsAccessDenied(SqlException ex)
    {
        if (ex == null) return false;
        foreach (SqlError err in ex.Errors)
        {
            if (err.Number is 3201 or 15105) return true;
            if (err.Message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)) return true;
            if (err.Message.Contains("Operating system error 5", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsCompressionUnsupported(SqlException ex)
    {
        if (ex == null) return false;
        foreach (SqlError err in ex.Errors)
        {
            if (err.Number == 1844) return true;
            if (err.Message.Contains("COMPRESSION is not supported", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // ═════════════════════════════════════════════════════════
    //  Logging
    // ═════════════════════════════════════════════════════════

    private void Log(string line)
    {
        try
        {
            if (!Directory.Exists(_backupPath)) Directory.CreateDirectory(_backupPath);
            File.AppendAllText(_logPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch { /* never throw from logger */ }
    }

    // ═════════════════════════════════════════════════════════
    //  USB helpers (keep DriveInfo quirks local)
    // ═════════════════════════════════════════════════════════

    private static string SafeLabel(DriveInfo d) { try { return d.VolumeLabel ?? ""; } catch { return ""; } }
    private static long SafeFree(DriveInfo d)    { try { return d.AvailableFreeSpace; } catch { return 0; } }
    private static long SafeTotal(DriveInfo d)   { try { return d.TotalSize; } catch { return 0; } }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:N1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):N1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):N2} GB";
    }
}
