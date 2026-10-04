using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ActivityMonitor.Infrastructure.Data;

public class ActivityMonitorDbContext : DbContext
{
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
    public DbSet<RecordingSegment> RecordingSegments => Set<RecordingSegment>();

    public ActivityMonitorDbContext()
    {
    }

    public ActivityMonitorDbContext(DbContextOptions<ActivityMonitorDbContext> options)
        : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var dbPath = ActivityMonitorPaths.DatabasePath;
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();

            optionsBuilder.UseSqlite(connectionString);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<RecordingSegment>(entity =>
        {
            entity.ToTable("RecordingSegments");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.MachineId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.FilePath).IsRequired().HasMaxLength(512);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Status).HasConversion<int>();

            entity.HasIndex(e => e.StartedAtUtc);
            entity.HasIndex(e => e.EndedAtUtc);
            entity.HasIndex(e => new { e.MachineId, e.StartedAtUtc });
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.FileName);
        });

        modelBuilder.Entity<ActivityEvent>(entity =>
        {
            entity.ToTable("ActivityEvents");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.MachineId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.UserIdentifier).IsRequired().HasMaxLength(128);
            entity.Property(e => e.ProcessName).IsRequired().HasMaxLength(256);
            entity.Property(e => e.ProcessPath).HasMaxLength(512);
            entity.Property(e => e.WindowTitle).HasMaxLength(512);
            entity.Property(e => e.ContextPath).HasMaxLength(1024);
            entity.Property(e => e.EventType).HasConversion<int>();

            entity.HasIndex(e => e.StartedAtUtc);
            entity.HasIndex(e => e.EndedAtUtc);
            entity.HasIndex(e => new { e.MachineId, e.StartedAtUtc });
            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => e.ProcessName);
            entity.HasIndex(e => e.RecordingSegmentId);

            entity.HasOne(e => e.RecordingSegment)
                  .WithMany(s => s.Activities)
                  .HasForeignKey(e => e.RecordingSegmentId)
                  .OnDelete(DeleteBehavior.SetNull);
        });
    }

    public static async Task InitializeDatabaseAsync(DbContextOptions<ActivityMonitorDbContext>? options = null, CancellationToken ct = default)
    {
        ActivityMonitorPaths.EnsureDirectoriesExist();

        var retryCount = 0;
        const int maxRetries = 3;

        while (true)
        {
            try
            {
                using var context = options != null ? new ActivityMonitorDbContext(options) : new ActivityMonitorDbContext();
                await context.Database.EnsureCreatedAsync(ct);

                // Enable Write-Ahead Logging (WAL) for superior concurrency and high write performance
                await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
                await context.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", ct);
                break;
            }
            catch (Exception) when (++retryCount <= maxRetries)
            {
                await Task.Delay(500 * retryCount, ct);
            }
        }
    }
}
