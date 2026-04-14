namespace RestaurantPOS.Application.Interfaces;

/// <summary>
/// Database maintenance operations — backup, restore, export, import.
/// </summary>
public interface IDatabaseMaintenanceService
{
    /// <summary>The file extension for backup files (e.g., ".bak").</summary>
    string BackupExtension { get; }

    /// <summary>File dialog filter string (e.g., "SQL Server Backup|*.bak").</summary>
    string FileFilter { get; }

    /// <summary>Get the database size as a display string.</summary>
    Task<string> GetDatabaseSizeAsync();

    /// <summary>Create a backup in the standard backup directory. Returns the backup file path.</summary>
    Task<string> BackupAsync();

    /// <summary>Restore the database from a backup file. App restart typically required.</summary>
    Task RestoreAsync(string backupFilePath);

    /// <summary>Export database to a user-chosen path.</summary>
    Task ExportAsync(string destinationPath);

    /// <summary>Import a backup file into the backup directory. Returns the destination path.</summary>
    Task<string> ImportAsync(string sourceFilePath);

    /// <summary>Flush pending writes before backup.</summary>
    Task FlushAsync();
}
