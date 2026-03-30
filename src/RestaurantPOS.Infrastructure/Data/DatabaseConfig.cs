using Microsoft.Data.Sqlite;

namespace RestaurantPOS.Infrastructure.Data;

public static class DatabaseConfig
{
    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RestaurantPOS");

    private static readonly string DbFilePath = Path.Combine(AppDataPath, "posdata.db");

    public static string GetDatabasePath() => DbFilePath;

    public static string GetConnectionString()
    {
        EnsureDirectoryExists();
        return new SqliteConnectionStringBuilder
        {
            DataSource = DbFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
      
    }

    private static void EnsureDirectoryExists()
    {
        if (!Directory.Exists(AppDataPath))
            Directory.CreateDirectory(AppDataPath);
    }
}
