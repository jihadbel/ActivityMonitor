using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ActivityMonitor.Infrastructure.Data;

internal sealed class SqliteConnectionPolicy : DbConnectionInterceptor
{
    public static readonly SqliteConnectionPolicy Instance = new();

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Apply(connection);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Apply(connection);
        return Task.CompletedTask;
    }

    private static void Apply(DbConnection connection)
    {
        if (connection is not SqliteConnection) return;
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=30000; PRAGMA foreign_keys=ON; PRAGMA synchronous=NORMAL;";
        command.ExecuteNonQuery();
    }
}
