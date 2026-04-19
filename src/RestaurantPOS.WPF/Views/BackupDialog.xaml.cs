using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using RestaurantPOS.Application.Interfaces;
using RestaurantPOS.Infrastructure.Data;

namespace RestaurantPOS.WPF.Views;

/// <summary>
/// Dedicated window for SQL Server database backup, import and export operations.
/// Uses <see cref="IDatabaseMaintenanceService"/> so the backing DB can be swapped
/// without touching this UI.
/// </summary>
public partial class BackupDialog : Window
{
    private readonly IDatabaseMaintenanceService _maintenance;
    private readonly string _backupPath;
    private bool _isBusy;

    /// <summary>True if any operation in this dialog added/removed backup files.</summary>
    public bool BackupListChanged { get; private set; }

    public BackupDialog(IDatabaseMaintenanceService maintenance)
    {
        InitializeComponent();
        _maintenance = maintenance;
        _backupPath = DatabaseConfig.GetBackupPath();
        Directory.CreateDirectory(_backupPath);

        TxtBackupPath.Text = _backupPath;
        _ = RefreshInfoAsync();
    }

    // ─────────────────────────────────────────────
    //  Info panel
    // ─────────────────────────────────────────────
    private async Task RefreshInfoAsync()
    {
        try
        {
            TxtDbSize.Text = await _maintenance.GetDatabaseSizeAsync();
        }
        catch
        {
            TxtDbSize.Text = "N/A";
        }

        try
        {
            var ext = _maintenance.BackupExtension;
            var files = Directory.Exists(_backupPath)
                ? Directory.GetFiles(_backupPath, $"*{ext}")
                    .OrderByDescending(File.GetLastWriteTime)
                    .ToArray()
                : [];

            TxtLastBackup.Text = files.Length > 0
                ? File.GetLastWriteTime(files[0]).ToString("dd/MM/yyyy hh:mm tt")
                : "Never";
        }
        catch
        {
            TxtLastBackup.Text = "Error";
        }

        // Initial status: show any detected USB drives so the user knows a copy will be made.
        try
        {
            var drives = _maintenance.GetUsbDrives();
            if (drives.Count == 0)
            {
                SetStatus("Ready",
                    "No USB drive detected. Backups will be saved locally only — plug a USB drive to also receive a copy.",
                    isError: false);
            }
            else
            {
                var names = string.Join(", ", drives.Select(d => d.Display));
                SetStatus("Ready",
                    $"USB drive{(drives.Count > 1 ? "s" : "")} detected: {names}. A backup copy will be placed in RestaurantPOS_Backups there.",
                    isError: false);
            }
        }
        catch { /* non-fatal */ }
    }

    // ─────────────────────────────────────────────
    //  Byte formatting helper
    // ─────────────────────────────────────────────
    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:0.##} {units[u]}";
    }

    // ─────────────────────────────────────────────
    //  Backup Now — production flow: rotation + USB copy
    // ─────────────────────────────────────────────
    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        SetBusy(true);
        SetStatus("Creating backup...",
            "Running BACKUP DATABASE, rotating old files, and copying to any connected USB drives.",
            isError: false);

        try
        {
            var result = await _maintenance.BackupWithRotationAsync(keepLast: 7);

            if (result.Success)
            {
                BackupListChanged = true;
                await RefreshInfoAsync();

                var sb = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(result.BackupFilePath))
                    sb.AppendLine($"Saved: {result.BackupFilePath}");
                sb.AppendLine($"Size: {FormatBytes(result.FileSizeBytes)}");
                if (result.RotatedCount > 0)
                    sb.AppendLine($"Rotated (deleted older): {result.RotatedCount}");
                if (!string.IsNullOrWhiteSpace(result.UsbMessage))
                    sb.AppendLine(result.UsbMessage);
                foreach (var copy in result.UsbCopies)
                    sb.AppendLine($"  • USB: {copy}");

                SetStatus("Backup complete", sb.ToString().TrimEnd(), isError: false);
            }
            else
            {
                SetStatus("Backup failed", result.Error ?? result.Message, isError: true);
            }
        }
        catch (Exception ex)
        {
            SetStatus("Backup failed", ex.Message, isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ─────────────────────────────────────────────
    //  Import Backup — smart merge (no overwrite)
    // ─────────────────────────────────────────────
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;

        var dialog = new OpenFileDialog
        {
            Filter = _maintenance.FileFilter + "|All files (*.*)|*.*",
            Title = "Select a backup file to smart-import"
        };
        if (dialog.ShowDialog() != true) return;

        var confirm = MessageBox.Show(
            "Smart Import will:\n" +
            "  • Restore the selected .bak into a temporary staging database\n" +
            "  • Insert only rows that are MISSING in the live database\n" +
            "  • Never overwrite or delete existing data\n\n" +
            "Continue?",
            "Confirm Smart Import",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        SetBusy(true);
        SetStatus("Smart-importing...",
            $"Restoring {Path.GetFileName(dialog.FileName)} to staging and merging new rows into the live database.",
            isError: false);

        try
        {
            var result = await _maintenance.SmartImportAsync(dialog.FileName);

            if (result.Success)
            {
                BackupListChanged = true;
                await RefreshInfoAsync();

                var sb = new StringBuilder();
                sb.AppendLine($"Tables compared: {result.TablesCompared}");
                sb.AppendLine($"Tables merged:   {result.TablesMerged}");
                sb.AppendLine($"Tables skipped:  {result.TablesSkipped}");
                sb.AppendLine($"Rows inserted:   {result.RowsInserted:N0}");
                if (result.Details.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Details:");
                    foreach (var d in result.Details.Take(12))
                        sb.AppendLine($"  • {d}");
                    if (result.Details.Count > 12)
                        sb.AppendLine($"  … (+{result.Details.Count - 12} more)");
                }

                SetStatus("Smart import complete", sb.ToString().TrimEnd(), isError: false);
            }
            else
            {
                SetStatus("Smart import failed", result.Error ?? result.Message, isError: true);
            }
        }
        catch (Exception ex)
        {
            SetStatus("Smart import failed", ex.Message, isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ─────────────────────────────────────────────
    //  Export (save current live DB to user's chosen location)
    // ─────────────────────────────────────────────
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;

        var dialog = new SaveFileDialog
        {
            Filter = _maintenance.FileFilter,
            FileName = $"posdata-export-{DateTime.Now:yyyyMMdd-HHmmss}{_maintenance.BackupExtension}",
            Title = "Export current database"
        };
        if (dialog.ShowDialog() != true) return;

        SetBusy(true);
        SetStatus("Exporting...", "Running BACKUP DATABASE to your chosen location...", isError: false);

        try
        {
            await _maintenance.ExportAsync(dialog.FileName);
            SetStatus("Export complete", $"Saved to: {dialog.FileName}", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus("Export failed", ex.Message, isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ─────────────────────────────────────────────
    //  Open backup folder in Explorer
    // ─────────────────────────────────────────────
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Directory.Exists(_backupPath)) Directory.CreateDirectory(_backupPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = _backupPath,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            SetStatus("Could not open folder", ex.Message, isError: true);
        }
    }

    // ─────────────────────────────────────────────
    //  UI helpers
    // ─────────────────────────────────────────────
    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        BtnBackup.IsEnabled = !busy;
        BtnImport.IsEnabled = !busy;
        BtnExport.IsEnabled = !busy;
        ProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ProgressBar.IsIndeterminate = busy;
        Cursor = busy ? Cursors.Wait : Cursors.Arrow;
    }

    private void SetStatus(string title, string message, bool isError)
    {
        TxtStatusTitle.Text = title;
        TxtStatusTitle.Foreground = isError
            ? System.Windows.Media.Brushes.Crimson
            : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#111827")!;
        TxtStatusMessage.Text = message;
    }

    // ─────────────────────────────────────────────
    //  Title bar + window chrome
    // ─────────────────────────────────────────────
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return; // Don't close mid-operation
        DialogResult = BackupListChanged;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_isBusy)
        {
            DialogResult = BackupListChanged;
            Close();
        }
    }
}
