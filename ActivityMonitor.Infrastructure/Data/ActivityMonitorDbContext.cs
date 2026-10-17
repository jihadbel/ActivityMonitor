using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Entities;
using ActivityMonitor.Core.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ActivityMonitor.Infrastructure.Data;

/// <summary>
/// سياق قاعدة بيانات الكيان (Entity Framework DbContext) لتطبيق ActivityMonitor،
/// يدير جداول أحداث الأنشطة ومقاطع تسجيل الفيديو، تهيئة مخطط SQLite، وضمان وضع WAL والتزامن الآمن عبر العمليات.
/// </summary>
public class ActivityMonitorDbContext : DbContext
{
    /// <summary>
    /// الإصدار الحالي لمخطط قاعدة البيانات للتحقق من التوافقية والترقية.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// جدول أحداث الأنشطة وتتبع النوافذ.
    /// </summary>
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();

    /// <summary>
    /// جدول مقاطع تسجيل شاشة الفيديو.
    /// </summary>
    public DbSet<RecordingSegment> RecordingSegments => Set<RecordingSegment>();

    /// <summary>
    /// منشئ فارغ افتراضي لسياق قاعدة البيانات.
    /// </summary>
    public ActivityMonitorDbContext()
    {
    }

    /// <summary>
    /// منشئ يستقبل خيارات تكوين سياق قاعدة البيانات من مصنع الكيانات (DbContextFactory).
    /// </summary>
    /// <param name="options">خيارات قاعدة البيانات.</param>
    public ActivityMonitorDbContext(DbContextOptions<ActivityMonitorDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// ضبط وتكوين اتصال قاعدة بيانات SQLite ومسار الملف والمهلة الزمنية في حال لم تكن مهيأة مسبقاً.
    /// </summary>
    /// <param name="optionsBuilder">باني خيارات السياق.</param>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(SqliteConnectionPolicy.Instance);
        if (!optionsBuilder.IsConfigured)
        {
            // استخراج مسار ملف قاعدة البيانات من الإعدادات المشتركة
            var dbPath = ActivityMonitorPaths.DatabasePath;
            var directory = Path.GetDirectoryName(dbPath);

            // التأكد من وجود مجلد قاعدة البيانات
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // إعداد نص اتصال SQLite مع مهلة انتظار 30 ثانية لتفادي قفل الملف
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                DefaultTimeout = 30
            }.ToString();

            // تفعيل استخدام محرك SQLite
            optionsBuilder.UseSqlite(connectionString);
        }
    }

    /// <summary>
    /// تكوين العلاقات والمفاتيح والفهارس والقيود على جداول الكيانات في نموذج قاعدة البيانات.
    /// </summary>
    /// <param name="modelBuilder">باني نموذج الكيانات.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ==========================================
        // تكوين جدول مقاطع تسجيل الفيديو (RecordingSegments)
        // ==========================================
        modelBuilder.Entity<RecordingSegment>(entity =>
        {
            entity.ToTable("RecordingSegments");
            entity.HasKey(e => e.Id);

            // قيود الأعمدة النصية والحجم
            entity.Property(e => e.MachineId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.FilePath).IsRequired().HasMaxLength(512);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.Status).IsConcurrencyToken();
            entity.HasIndex(e => new { e.MachineId, e.SessionId, e.StartedAtUtc });
            entity.HasIndex(e => e.HeartbeatAtUtc);

            // فهارس لتحسين سرعة الاستعلام والبحث الزمني
            entity.HasIndex(e => e.StartedAtUtc);
            entity.HasIndex(e => e.EndedAtUtc);
            entity.HasIndex(e => new { e.MachineId, e.StartedAtUtc });
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.FileName);
        });

        // ==========================================
        // تكوين جدول أحداث الأنشطة (ActivityEvents)
        // ==========================================
        modelBuilder.Entity<ActivityEvent>(entity =>
        {
            entity.ToTable("ActivityEvents");
            entity.HasKey(e => e.Id);

            // قيود الأعمدة النصية
            entity.Property(e => e.MachineId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.UserIdentifier).IsRequired().HasMaxLength(128);
            entity.Property(e => e.ProcessName).IsRequired().HasMaxLength(256);
            entity.Property(e => e.ProcessPath).HasMaxLength(512);
            entity.Property(e => e.WindowTitle).HasMaxLength(512);
            entity.Property(e => e.ContextPath).HasMaxLength(1024);
            entity.Property(e => e.EventType).HasConversion<int>();

            // فهارس للبحث السريع بحسب التاريخ، الجهاز، العملية، ومقطع الفيديو
            entity.HasIndex(e => e.StartedAtUtc);
            entity.HasIndex(e => e.EndedAtUtc);
            entity.HasIndex(e => new { e.MachineId, e.StartedAtUtc });
            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => e.ProcessName);
            entity.HasIndex(e => e.RecordingSegmentId);

            // العلاقة بين حدث النشاط ومقطع التسجيل: مفتاح خارجي مع تعيين null عند الحذف
            entity.HasOne(e => e.RecordingSegment)
                  .WithMany(s => s.Activities)
                  .HasForeignKey(e => e.RecordingSegmentId)
                  .OnDelete(DeleteBehavior.SetNull);
        });
    }

    /// <summary>
    /// تهيئة قاعدة البيانات وإنشاء الجداول وتفعيل وضع WAL وضمان عدم تعارض العمليات عبر قفل نظامي شامل (Mutex).
    /// </summary>
    /// <param name="options">خيارات السياق المخصصة (اختياري).</param>
    /// <param name="ct">رمز إلغاء العملية.</param>
    /// <returns>مهمة تمثل العملية غير المتزامنة.</returns>
    public static async Task InitializeDatabaseAsync(DbContextOptions<ActivityMonitorDbContext>? options = null, CancellationToken ct = default)
    {
        // File ownership is independent of threads, unlike Mutex ownership across an await.
        using var probe = options != null ? new ActivityMonitorDbContext(options) : new ActivityMonitorDbContext();
        var databasePath = probe.Database.GetDbConnection().DataSource;
        if (string.IsNullOrEmpty(databasePath) || databasePath == ":memory:")
        {
            await probe.Database.EnsureCreatedAsync(ct);
            return;
        }
        databasePath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        FileStream? gate = null;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (gate == null)
        {
            ct.ThrowIfCancellationRequested();
            try { gate = new FileStream(databasePath + ".init.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline) { await Task.Delay(100, ct); }
        }
        await using (gate)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await using var context = options != null ? new ActivityMonitorDbContext(options) : new ActivityMonitorDbContext();
                    await context.Database.EnsureCreatedAsync(ct);
                    await ValidateAndUpgradeSchemaAsync(context, ct);
                    await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
                    return;
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6 && attempt < 5)
                {
                    await Task.Delay(500 * (attempt + 1), ct);
                }
            }
        }
    }

    /// <summary>
    /// فحص رقم إصدار المخطط والأعمدة المطلوبة والتحقق من عدم وجود قاعدة بيانات من إصدار أحدث غير مدعوم.
    /// </summary>
    /// <param name="context">سياق قاعدة البيانات.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    private static async Task ValidateAndUpgradeSchemaAsync(
        ActivityMonitorDbContext context,
        CancellationToken ct)
    {
        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            // قراءة رقم إصدار المخطط من SQLite PRAGMA user_version
            await using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "PRAGMA user_version;";
            var versionValue = await versionCommand.ExecuteScalarAsync(ct);
            var databaseVersion = Convert.ToInt32(versionValue ?? 0);

            // إذا كان إصدار قاعدة البيانات أحدث من التطبيق نرفض الفتح لتجنب إتلاف البيانات
            if (databaseVersion > CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Database schema version {databaseVersion} is newer than supported version {CurrentSchemaVersion}. " +
                    "Upgrade ActivityMonitor before opening this database.");
            }

            // التحقق من وجود كافة أعمدة جدول أحداث الأنشطة
            if (databaseVersion < 2)
            {
                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                await using (var inspect = connection.CreateCommand())
                {
                    inspect.CommandText = "PRAGMA table_info('RecordingSegments');";
                    await using var reader = await inspect.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(1));
                }
                // SQLite's online backup includes committed WAL data. Never migrate without a backup.
                if ((databaseVersion > 0 || !columns.Contains("OwnerInstanceId")) && connection is SqliteConnection sqlite)
                {
                    var backupPath = DatabasePathForBackup(sqlite.DataSource);
                    await using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath }.ToString());
                    await backup.OpenAsync(ct);
                    sqlite.BackupDatabase(backup);
                }
                await using var transaction = await connection.BeginTransactionAsync(ct);
                foreach (var (name, type) in new[] {
                    ("UserIdentifier", "TEXT NOT NULL DEFAULT ''"), ("OwnerInstanceId", "TEXT NOT NULL DEFAULT ''"),
                    ("OwnerProcessId", "INTEGER NOT NULL DEFAULT 0"), ("OwnerProcessStartedAtUtc", "TEXT NULL"),
                    ("HeartbeatAtUtc", "TEXT NULL") })
                {
                    if (columns.Contains(name)) continue;
                    await using var alter = connection.CreateCommand();
                    alter.Transaction = transaction;
                    alter.CommandText = $"ALTER TABLE RecordingSegments ADD COLUMN {name} {type};";
                    await alter.ExecuteNonQueryAsync(ct);
                }
                await using var index = connection.CreateCommand();
                index.Transaction = transaction;
                index.CommandText = "CREATE INDEX IF NOT EXISTS IX_RecordingSegments_MachineId_SessionId_StartedAtUtc ON RecordingSegments(MachineId, SessionId, StartedAtUtc); CREATE INDEX IF NOT EXISTS IX_RecordingSegments_HeartbeatAtUtc ON RecordingSegments(HeartbeatAtUtc);";
                await index.ExecuteNonQueryAsync(ct);
                await transaction.CommitAsync(ct);
            }

            await ValidateRequiredColumnsAsync(
                connection,
                "ActivityEvents",
                [
                    "Id", "MachineId", "SessionId", "UserIdentifier", "ProcessName", "ProcessId",
                    "ProcessPath", "WindowTitle", "ContextPath", "EventType", "StartedAtUtc", "EndedAtUtc",
                    "DurationSeconds", "RecordingSegmentId", "CreatedAtUtc"
                ],
                ct);

            // التحقق من وجود كافة أعمدة جدول مقاطع التسجيل
            await ValidateRequiredColumnsAsync(
                connection,
                "RecordingSegments",
                [
                    "Id", "MachineId", "SessionId", "StartedAtUtc", "EndedAtUtc", "DurationSeconds",
                    "FilePath", "FileName", "FileSizeBytes", "Status", "CreatedAtUtc",
                    "UserIdentifier", "OwnerInstanceId", "OwnerProcessId", "OwnerProcessStartedAtUtc", "HeartbeatAtUtc"
                ],
                ct);

            // إذا كانت قاعدة بيانات جديدة برقم إصدار 0، نسجل رقم الإصدار الحالي
            if (databaseVersion < CurrentSchemaVersion)
            {
                await using var stampCommand = connection.CreateCommand();
                stampCommand.CommandText = $"PRAGMA user_version={CurrentSchemaVersion};";
                await stampCommand.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// التحقق المباشر من وجود كافة الأعمدة المطلوبة داخل جدول معين باستخدام PRAGMA table_info.
    /// </summary>
    /// <param name="connection">اتصال قاعدة البيانات المفتوح.</param>
    /// <param name="tableName">اسم الجدول المراد فحصه.</param>
    /// <param name="requiredColumns">مجموعة الأعمدة الإلزامية.</param>
    /// <param name="ct">رمز الإلغاء.</param>
    private static string DatabasePathForBackup(string databasePath)
    {
        var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, "Backups");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"schema-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.db");
    }

    private static async Task ValidateRequiredColumnsAsync(
        System.Data.Common.DbConnection connection,
        string tableName,
        IReadOnlyCollection<string> requiredColumns,
        CancellationToken ct)
    {
        var actualColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{tableName}');";
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            actualColumns.Add(reader.GetString(1));
        }

        // كشف أي أعمدة مفقودة
        var missingColumns = requiredColumns.Where(column => !actualColumns.Contains(column)).ToArray();
        if (missingColumns.Length > 0)
        {
            throw new InvalidDataException(
                $"Database table '{tableName}' is missing required columns: {string.Join(", ", missingColumns)}. " +
                "Back up the database and run a supported schema upgrade.");
        }
    }
}
