using Microsoft.Data.Sqlite;

namespace ActivityMonitor.Infrastructure.Services;

public static class DatabaseBackupService
{
    public static async Task BackupAsync(string sourcePath, string destinationPath, CancellationToken ct = default)
    {
        var source = Path.GetFullPath(sourcePath);
        var destination = Path.GetFullPath(destinationPath);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Backup must use a different path.");
        if (!File.Exists(source)) throw new FileNotFoundException("Database does not exist.", source);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var input = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            await using (var output = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString()))
            {
                await input.OpenAsync(ct); await output.OpenAsync(ct);
                input.BackupDatabase(output);
                await using var check = output.CreateCommand(); check.CommandText = "PRAGMA quick_check;";
                if (!string.Equals(Convert.ToString(await check.ExecuteScalarAsync(ct)), "ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Backup failed SQLite integrity verification.");
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination, false); // Never silently overwrite an existing backup.
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
