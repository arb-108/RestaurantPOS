using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace RestaurantPOS.Infrastructure.Data;

// ═══════════════════════════════════════════════════════════
//  SQL SERVER DATABASE CONFIGURATION
//  Settings stored in %LOCALAPPDATA%/RestaurantPOS/dbconfig.json
// ═══════════════════════════════════════════════════════════

/// <summary>
/// Centralized database configuration for SQL Server.
/// Reads connection settings from %LOCALAPPDATA%/RestaurantPOS/dbconfig.json.
/// If the file is missing, creates one with sensible defaults.
/// </summary>
public static class DatabaseConfig
{
    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RestaurantPOS");

    private static readonly string ConfigFilePath = Path.Combine(AppDataPath, "dbconfig.json");

    private static DbConfigSettings? _cached;

    // ── Public API ──

    /// <summary>Get the SQL Server connection string.</summary>
    public static string GetConnectionString()
    {
        var settings = GetSettings();
        return BuildConnectionString(settings);
    }

    /// <summary>Configure the DbContextOptionsBuilder for SQL Server.</summary>
    public static void ConfigureDbContext(DbContextOptionsBuilder options)
    {
        var connectionString = GetConnectionString();
        options.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.CommandTimeout(30);
            sqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
        });

        // WPF desktop: async continuations on UI thread interleave but don't
        // truly run in parallel. MARS handles interleaved commands on the
        // SQL Server connection; disable EF Core's overly-strict check.
        options.EnableThreadSafetyChecks(false);
    }

    /// <summary>Get the backup directory path.</summary>
    public static string GetBackupPath() => Path.Combine(AppDataPath, "backups");

    /// <summary>Get a copy of the current settings.</summary>
    public static DbConfigSettings GetSettings()
    {
        if (_cached != null) return _cached;

        EnsureDirectoryExists();

        if (File.Exists(ConfigFilePath))
        {
            try
            {
                var json = File.ReadAllText(ConfigFilePath);
                var settings = JsonSerializer.Deserialize<DbConfigSettings>(json, _jsonOptions);
                if (settings != null)
                {
                    _cached = settings;
                    return _cached;
                }
            }
            catch { /* Fall through to defaults */ }
        }

        // Default: localhost default instance (SQL Server 2022 Express)
        _cached = new DbConfigSettings();

        // Save defaults so user can edit the file
        SaveSettings(_cached);
        return _cached;
    }

    /// <summary>Save settings to dbconfig.json.</summary>
    public static void SaveSettings(DbConfigSettings settings)
    {
        EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(ConfigFilePath, json);
        _cached = settings;
    }

    /// <summary>Reset cache (useful after editing dbconfig.json externally).</summary>
    public static void ResetCache() => _cached = null;

    // ── Private helpers ──

    private static string BuildConnectionString(DbConfigSettings settings)
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        {
            DataSource = settings.Server,
            InitialCatalog = settings.Database,
            IntegratedSecurity = settings.IntegratedSecurity,
            TrustServerCertificate = true,
            MultipleActiveResultSets = true,
            ConnectTimeout = 15
        };

        // If not using Windows Auth, use SQL Auth
        if (!settings.IntegratedSecurity)
        {
            builder.UserID = settings.Username ?? "sa";
            builder.Password = settings.Password ?? "";
        }

        return builder.ToString();
    }

    private static void EnsureDirectoryExists()
    {
        if (!Directory.Exists(AppDataPath))
            Directory.CreateDirectory(AppDataPath);
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

// ═══════════════════════════════════════════════════════════
//  CONFIGURATION MODEL
// ═══════════════════════════════════════════════════════════

public class DbConfigSettings
{
    public string Server { get; set; } = ".";
    public string Database { get; set; } = "RestaurantPOS";
    public bool IntegratedSecurity { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
}
