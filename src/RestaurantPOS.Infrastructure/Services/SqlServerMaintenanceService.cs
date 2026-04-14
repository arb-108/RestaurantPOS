using Microsoft.EntityFrameworkCore;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Infrastructure.Data;

namespace RestaurantPOS.Infrastructure.Services;

/// <summary>
/// SQL Server backup/restore using BACKUP DATABASE / RESTORE DATABASE T-SQL commands.
/// </summary>
public class SqlServerMaintenanceService : IDatabaseMaintenanceService
{
    private readonly PosDbContext _db;
    private readonly string _backupPath;

    public SqlServerMaintenanceService(PosDbContext db)
    {
        _db = db;
        _backupPath = DatabaseConfig.GetBackupPath();
    }

    public string BackupExtension => ".bak";
    public string FileFilter => "SQL Server Backup|*.bak";

    public async Task<string> GetDatabaseSizeAsync()
    {
        try
        {
            var conn = _db.Database.GetDbConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT CAST(SUM(size) * 8.0 / 1024 AS DECIMAL(10,1)) AS SizeMB
                FROM sys.database_files";
            var result = await cmd.ExecuteScalarAsync();
            if (result != null && result != DBNull.Value)
                return $"{result} MB";
        }
        catch { /* Ignore — may not have permission */ }
        return "N/A";
    }

    public Task FlushAsync()
    {
        // SQL Server manages its own write-ahead log; no manual flush needed
        return Task.CompletedTask;
    }

    public async Task<string> BackupAsync()
    {
        if (!Directory.Exists(_backupPath)) Directory.CreateDirectory(_backupPath);

        var settings = DatabaseConfig.GetSettings();
        var dbName = settings.Database;
        var backupFile = Path.Combine(_backupPath, $"{dbName}-{DateTime.Now:yyyyMMdd-HHmmss}.bak");

        var sql = $"BACKUP DATABASE [{dbName}] TO DISK = N'{backupFile}' WITH FORMAT, INIT, NAME = N'POS Backup', COMPRESSION";
        await _db.Database.ExecuteSqlRawAsync(sql);

        return backupFile;
    }

    public async Task RestoreAsync(string backupFilePath)
    {
        var settings = DatabaseConfig.GetSettings();
        var dbName = settings.Database;

        // Safety backup first
        try { await BackupAsync(); } catch { /* Best effort */ }

        // Set to single user, restore, then multi user
        var sql = $@"
            ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            RESTORE DATABASE [{dbName}] FROM DISK = N'{backupFilePath}' WITH REPLACE;
            ALTER DATABASE [{dbName}] SET MULTI_USER;";

        // Must use master database for restore
        var masterConn = DatabaseConfig.GetConnectionString()
            .Replace($"Initial Catalog={dbName}", "Initial Catalog=master");
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(masterConn);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 120;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ExportAsync(string destinationPath)
    {
        var settings = DatabaseConfig.GetSettings();
        var dbName = settings.Database;

        var sql = $"BACKUP DATABASE [{dbName}] TO DISK = N'{destinationPath}' WITH FORMAT, INIT, NAME = N'POS Export', COMPRESSION";
        await _db.Database.ExecuteSqlRawAsync(sql);
    }

    public Task<string> ImportAsync(string sourceFilePath)
    {
        if (!Directory.Exists(_backupPath)) Directory.CreateDirectory(_backupPath);
        var destFile = Path.Combine(_backupPath, Path.GetFileName(sourceFilePath));
        File.Copy(sourceFilePath, destFile, true);
        return Task.FromResult(destFile);
    }
}
