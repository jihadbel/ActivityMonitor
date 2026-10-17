using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Options;
using Microsoft.Extensions.Logging;

namespace ActivityMonitor.Infrastructure.Logging;

/// <summary>
/// مزود خدمة تسجيل السجلات في ملفات نصية (File Logger Provider)،
/// يستخدم طابور رسائل خلفي غير متزامن وخيط كتابة مخصص لتفادي تجميد واجهة المستخدم أو العمليات الحيوية.
/// </summary>
public class ActivityFileLoggerProvider : ILoggerProvider
{
    /// <summary>
    /// اسم المكون التابع له المسجل (مثل Agent أو Service أو Viewer).
    /// </summary>
    private readonly string _componentName;

    /// <summary>
    /// طابور حظر الرسائل المتزامن بسعة قصوى 5000 رسالة لحماية الذاكرة.
    /// </summary>
    private readonly BlockingCollection<string> _messageQueue = new(new ConcurrentQueue<string>(), 5000);

    /// <summary>
    /// مصدر رموز الإلغاء لإيقاف خيط الكتابة بسلاسة.
    /// </summary>
    private long _droppedMessages, _writeFailures;
    private int _disposed;
    public long DroppedMessages => Interlocked.Read(ref _droppedMessages);
    public long WriteFailures => Interlocked.Read(ref _writeFailures);

    /// <summary>
    /// خيط معالجة خلفي مخصص لكتابة الرسائل في ملفات السجل على القرص.
    /// </summary>
    private readonly Thread _writeThread;

    /// <summary>
    /// منشئ الفئة: يبدأ خيط الكتابة الخلفي للمكون المحدد.
    /// </summary>
    /// <param name="componentName">اسم المكون.</param>
    public ActivityFileLoggerProvider(string componentName)
    {
        _componentName = componentName;

        // إنشاء خيط مخصص لكتابة السجلات في الخلفية
        _writeThread = new Thread(ProcessLogQueue)
        {
            IsBackground = true,
            Name = $"{componentName}LogWriter"
        };
        _writeThread.Start();
    }

    /// <summary>
    /// إنشاء كائن مسجل لفئة أو فئة فرعية محددة.
    /// </summary>
    /// <param name="categoryName">اسم الفئة المسجلة.</param>
    /// <returns>كائن ActivityFileLogger.</returns>
    public ILogger CreateLogger(string categoryName)
    {
        return new ActivityFileLogger(_componentName, categoryName, this);
    }

    /// <summary>
    /// إدراج رسالة سجل جديدة في طابور الكتابة الخلفي.
    /// </summary>
    /// <param name="message">نص السجل المنسق.</param>
    internal void EnqueueMessage(string message)
    {
        try
        {
            if (Volatile.Read(ref _disposed) != 0 || !_messageQueue.TryAdd(message)) Interlocked.Increment(ref _droppedMessages);
        }
        catch (InvalidOperationException) { Interlocked.Increment(ref _droppedMessages); }
    }

    /// <summary>
    /// الحلقة المستمرة لخيط الكتابة لسحب الرسائل من الطابور وحفظها في ملف اليوم.
    /// </summary>
    private void ProcessLogQueue()
    {
        try
        {
            foreach (var first in _messageQueue.GetConsumingEnumerable())
            {
                var batch = new List<string>(256) { first };
                while (batch.Count < 256 && _messageQueue.TryTake(out var next)) batch.Add(next);
                try
                {
                    var path = ActivityMonitorPaths.GetLogFilePath(_componentName, DateTime.UtcNow);
                    File.AppendAllLines(path, batch, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    Interlocked.Add(ref _writeFailures, batch.Count);
                    System.Diagnostics.Trace.TraceError("ActivityMonitor file logging failed: {0}", ex.Message);
                }
            }
        }
        finally { _messageQueue.Dispose(); }
    }

    /// <summary>
    /// إيقاف الطابور وتفريغ الرسائل المتبقية والتخلص من الموارد.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _messageQueue.CompleteAdding();
        _writeThread.Join(5000);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// مسجل أحداث يتبع واجهة ILogger ويكتب السجلات المنسقة عبر مزود الملفات.
/// </summary>
public class ActivityFileLogger : ILogger
{
    private readonly string _componentName;
    private readonly string _categoryName;
    private readonly ActivityFileLoggerProvider _provider;

    /// <summary>
    /// منشئ مسجل الملفات.
    /// </summary>
    /// <param name="componentName">اسم المكون.</param>
    /// <param name="categoryName">اسم فئة السجل.</param>
    /// <param name="provider">مزود خدمة التسجيل.</param>
    public ActivityFileLogger(string componentName, string categoryName, ActivityFileLoggerProvider provider)
    {
        _componentName = componentName;
        _categoryName = categoryName;
        _provider = provider;
    }

    /// <summary>
    /// بدء نطاق تتبع (Scope) - غير مستخدم في مسجل الملفات البسيط.
    /// </summary>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <summary>
    /// التحقق مما إذا كان مستوى السجل مفعلاً (الافتراضي: Information وأعلى).
    /// </summary>
    /// <param name="logLevel">مستوى السجل.</param>
    /// <returns>القيمة true إذا كان مفعلاً؛ وإلا false.</returns>
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    /// <summary>
    /// تسجيل رسالة واستثناء وتنسيقها مع الطابع الزمني وإرسالها لطابور الكتابة.
    /// </summary>
    /// <typeparam name="TState">نوع كائن الحالة.</typeparam>
    /// <param name="logLevel">مستوى الرسالة.</param>
    /// <param name="eventId">معرّف الحدث.</param>
    /// <param name="state">كائن الحالة.</param>
    /// <param name="exception">الاستثناء المرافق إن وجد.</param>
    /// <param name="formatter">دالة تنسيق الرسالة.</param>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        // صياغة الرسالة مع الطابع الزمني ومستوى الأهمية والفئة
        var message = formatter(state, exception);
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logLine = $"[{timestamp}] [{logLevel,-5}] [{_categoryName}] {message}";

        // إرفاق تفاصيل الاستثناء إن وجدت
        if (exception != null)
        {
            logLine += Environment.NewLine + exception.ToString();
        }

        // إرسال السطر إلى طابور المزود
        _provider.EnqueueMessage(logLine);
    }
}

/// <summary>
/// دوال توسيعية (Extension Methods) لإضافة مسجل ملفات النشاط بسهولة أثناء تكوين المضيف.
/// </summary>
public static class ActivityFileLoggerExtensions
{
    /// <summary>
    /// إضافة مسجل الملفات اليومية لمكون محدد إلى باني السجلات.
    /// </summary>
    /// <param name="builder">باني السجلات.</param>
    /// <param name="componentName">اسم المكون.</param>
    /// <returns>باني السجلات بعد إضافة المزود.</returns>
    public static ILoggingBuilder AddActivityFileLogger(this ILoggingBuilder builder, string componentName)
    {
        builder.AddProvider(new ActivityFileLoggerProvider(componentName));
        return builder;
    }
}
