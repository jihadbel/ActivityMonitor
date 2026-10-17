using ActivityMonitor.Core.Options;
using ActivityMonitor.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ActivityMonitor.Tests;

[Collection(ActivityMonitorPathsCollection.Name)]
public class DatabaseInitializationTests
{
    [Fact]
    public async Task InitializeDatabaseAsync_ShouldStampCurrentSchemaVersion()
    {
        var (tempRoot, options) = CreateTemporaryDatabase();
        try
        {
            await ActivityMonitorDbContext.InitializeDatabaseAsync(options);

            await using var context = new ActivityMonitorDbContext(options);
            await context.Database.OpenConnectionAsync();
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA user_version;";

            Assert.Equal((long)ActivityMonitorDbContext.CurrentSchemaVersion, Convert.ToInt64(await command.ExecuteScalarAsync()));
        }
        finally
        {
            Cleanup(tempRoot);
        }
    }

    [Fact]
    public async Task InitializeDatabaseAsync_ShouldRejectNewerSchemaVersion()
    {
        var (tempRoot, options) = CreateTemporaryDatabase();
        try
        {
            await using (var context = new ActivityMonitorDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();
                await context.Database.ExecuteSqlRawAsync("PRAGMA user_version=999;");
            }

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => ActivityMonitorDbContext.InitializeDatabaseAsync(options));

            Assert.Contains("newer than supported", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(tempRoot);
        }
    }

    private static (string TempRoot, DbContextOptions<ActivityMonitorDbContext> Options) CreateTemporaryDatabase()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ActivityMonitor_DbTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        ActivityMonitorPaths.SetCustomRootDirectory(tempRoot);

        var databasePath = Path.Combine(tempRoot, "activity.db");
        var options = new DbContextOptionsBuilder<ActivityMonitorDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        return (tempRoot, options);
    }

    private static void Cleanup(string tempRoot)
    {
        ActivityMonitorPaths.SetCustomRootDirectory(null);
        if (Directory.Exists(tempRoot))
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }
}
