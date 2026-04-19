namespace RestaurantPOS.Application.Interfaces;

/// <summary>
/// Database maintenance operations — backup, restore, export, smart-import.
/// </summary>
public interface IDatabaseMaintenanceService
{
    /// <summary>The file extension for backup files (e.g., ".bak").</summary>
    string BackupExtension { get; }

    /// <summary>File dialog filter string (e.g., "SQL Server Backup|*.bak").</summary>
    string FileFilter { get; }

    /// <summary>Get the database size as a display string.</summary>
    Task<string> GetDatabaseSizeAsync();

    /// <summary>Flush pending writes before backup.</summary>
    Task FlushAsync();

    // ─────────── Legacy simple API (kept for backward compat) ───────────

    /// <summary>Create a backup in the standard backup directory. Returns the backup file path.</summary>
    Task<string> BackupAsync();

    /// <summary>Restore the database from a backup file (FULL replace). App restart typically required.</summary>
    Task RestoreAsync(string backupFilePath);

    /// <summary>Export database to a user-chosen path.</summary>
    Task ExportAsync(string destinationPath);

    /// <summary>Import a backup file into the backup directory. Returns the destination path.</summary>
    Task<string> ImportAsync(string sourceFilePath);

    // ─────────── New production-grade API ───────────

    /// <summary>
    /// Full production backup flow:
    ///   1. Back up to ProgramData\RestaurantPOS\Backups\backup_yyyyMMdd_HHmm.bak
    ///   2. Prune older .bak files, keeping only <paramref name="keepLast"/>
    ///   3. Auto-detect USB drives and copy the backup to &lt;USB&gt;\RestaurantPOS_Backups\
    ///   4. Log every step to backup.log
    /// Returns a structured <see cref="BackupResult"/> (never throws for normal errors).
    /// </summary>
    Task<BackupResult> BackupWithRotationAsync(int keepLast = 7);

    /// <summary>
    /// Smart import: restores a .bak to a staging database, compares schemas with the
    /// live DB, and only inserts rows that DO NOT already exist in the live DB.
    /// Tables missing from either DB are skipped. Primary-key comparison is used where
    /// possible; otherwise all columns are compared.
    /// </summary>
    Task<SmartImportResult> SmartImportAsync(string bakFilePath);

    /// <summary>Enumerate currently-mounted removable (USB) drives.</summary>
    IReadOnlyList<UsbDriveInfo> GetUsbDrives();
}

// ═══════════════════════════════════════════════════════════
//  RESULT RECORDS
// ═══════════════════════════════════════════════════════════

/// <summary>Outcome of a production backup run.</summary>
public sealed class BackupResult
{
    public bool Success { get; init; }
    public string? BackupFilePath { get; init; }
    public long FileSizeBytes { get; init; }
    public int RotatedCount { get; init; }
    public string UsbMessage { get; init; } = "";
    public List<string> UsbCopies { get; init; } = new();
    public string Message { get; init; } = "";
    public string? Error { get; init; }
}

/// <summary>Outcome of a smart-import (merge) run.</summary>
public sealed class SmartImportResult
{
    public bool Success { get; init; }
    public int TablesCompared { get; init; }
    public int TablesMerged { get; init; }
    public int TablesSkipped { get; init; }
    public long RowsInserted { get; init; }
    public List<string> Details { get; init; } = new();
    public string Message { get; init; } = "";
    public string? Error { get; init; }
}

/// <summary>Lightweight info about a USB drive, free of WPF dependencies.</summary>
public sealed class UsbDriveInfo
{
    public string RootDirectory { get; init; } = "";
    public string VolumeLabel { get; init; } = "";
    public long AvailableFreeBytes { get; init; }
    public long TotalBytes { get; init; }
    public string Display => string.IsNullOrWhiteSpace(VolumeLabel)
        ? RootDirectory
        : $"{RootDirectory} ({VolumeLabel})";
}
