using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Data.SqlClient;
using RestaurantPOS.Infrastructure.Data;

namespace RestaurantPOS.WPF.Views;

/// <summary>
/// Dedicated dialog for configuring and testing the SQL Server connection used by the app.
/// Persists settings to %LOCALAPPDATA%\RestaurantPOS\dbconfig.json via <see cref="DatabaseConfig"/>.
/// </summary>
public partial class DatabaseSettingsDialog : Window
{
    private bool _isBusy;

    /// <summary>True if the user saved new settings (owner should prompt restart).</summary>
    public bool SettingsSaved { get; private set; }

    public DatabaseSettingsDialog()
    {
        InitializeComponent();
        LoadSettings();
    }

    // ─────────────────────────────────────────────
    //  Load / bind
    // ─────────────────────────────────────────────
    private void LoadSettings()
    {
        var s = DatabaseConfig.GetSettings();
        TxtServer.Text = s.Server;
        TxtDatabase.Text = s.Database;
        CmbAuth.SelectedIndex = s.IntegratedSecurity ? 0 : 1;
        TxtUsername.Text = s.Username ?? "";
        TxtPassword.Password = s.Password ?? "";
        UpdateAuthFieldsEnabled();
    }

    private void UpdateAuthFieldsEnabled()
    {
        var isWindows = CmbAuth.SelectedIndex == 0;
        TxtUsername.IsEnabled = !isWindows;
        TxtPassword.IsEnabled = !isWindows;
        TxtUsername.Opacity = isWindows ? 0.55 : 1.0;
        TxtPassword.Opacity = isWindows ? 0.55 : 1.0;
    }

    private void CmbAuth_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateAuthFieldsEnabled();
    }

    // ─────────────────────────────────────────────
    //  Test Connection
    // ─────────────────────────────────────────────
    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;

        var server = TxtServer.Text.Trim();
        var database = TxtDatabase.Text.Trim();
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
        {
            SetStatus("Missing information", "Please enter both the SQL Server name and the database name.", isError: true);
            return;
        }

        SetBusy(true);
        SetStatus("Testing connection...", $"Connecting to [{server}].[{database}] ...", isError: false);

        try
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = "master", // test against master so we can report DB existence separately
                IntegratedSecurity = CmbAuth.SelectedIndex == 0,
                TrustServerCertificate = true,
                ConnectTimeout = 10,
            };
            if (!builder.IntegratedSecurity)
            {
                builder.UserID = TxtUsername.Text.Trim();
                builder.Password = TxtPassword.Password;
            }

            var (ok, message, dbExists) = await Task.Run(async () =>
            {
                try
                {
                    await using var conn = new SqlConnection(builder.ConnectionString);
                    await conn.OpenAsync();
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT DB_ID(@name)";
                    cmd.Parameters.AddWithValue("@name", database);
                    var exists = await cmd.ExecuteScalarAsync();
                    var dbFound = exists != null && exists != DBNull.Value;
                    return (true, "Server is reachable.", dbFound);
                }
                catch (Exception ex)
                {
                    return (false, ex.Message, false);
                }
            });

            if (ok)
            {
                if (dbExists)
                {
                    SetStatus("Connection OK",
                        $"Server reachable. Database '{database}' found. You can save these settings.",
                        isError: false);
                }
                else
                {
                    SetStatus("Server OK, database missing",
                        $"Server reachable, but database '{database}' does not exist yet. " +
                        "It will be created automatically on next app start.",
                        isError: false);
                }
            }
            else
            {
                SetStatus("Connection failed", message, isError: true);
            }
        }
        catch (Exception ex)
        {
            SetStatus("Connection failed", ex.Message, isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ─────────────────────────────────────────────
    //  Save & Close
    // ─────────────────────────────────────────────
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;

        var server = TxtServer.Text.Trim();
        var database = TxtDatabase.Text.Trim();
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
        {
            SetStatus("Missing information", "Please enter both the SQL Server name and the database name.", isError: true);
            return;
        }

        var isWindows = CmbAuth.SelectedIndex == 0;
        if (!isWindows && string.IsNullOrWhiteSpace(TxtUsername.Text))
        {
            SetStatus("Missing credentials", "SQL Server authentication requires a user name.", isError: true);
            return;
        }

        try
        {
            var settings = new DbConfigSettings
            {
                Server = server,
                Database = database,
                IntegratedSecurity = isWindows,
                Username = isWindows ? null : TxtUsername.Text.Trim(),
                Password = isWindows ? null : TxtPassword.Password
            };
            DatabaseConfig.SaveSettings(settings);
            DatabaseConfig.ResetCache();

            SettingsSaved = true;

            MessageBox.Show(
                "Database settings saved.\n\nPlease restart the application for the changes to take effect.",
                "Saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            SetStatus("Save failed", ex.Message, isError: true);
        }
    }

    // ─────────────────────────────────────────────
    //  UI helpers
    // ─────────────────────────────────────────────
    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        BtnTest.IsEnabled = !busy;
        BtnSave.IsEnabled = !busy;
        ProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ProgressBar.IsIndeterminate = busy;
        Cursor = busy ? Cursors.Wait : Cursors.Arrow;
    }

    private void SetStatus(string title, string message, bool isError)
    {
        TxtStatusTitle.Text = title;
        TxtStatusTitle.Foreground = isError
            ? System.Windows.Media.Brushes.Crimson
            : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#065F46")!;
        TxtStatusMessage.Text = message;
    }

    // ─────────────────────────────────────────────
    //  Window chrome
    // ─────────────────────────────────────────────
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        DialogResult = SettingsSaved;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_isBusy)
        {
            DialogResult = SettingsSaved;
            Close();
        }
    }
}
