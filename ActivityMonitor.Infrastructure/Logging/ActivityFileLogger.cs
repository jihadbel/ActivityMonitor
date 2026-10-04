using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ActivityMonitor.Core.Options;
using Microsoft.Extensions.Logging;

namespace ActivityMonitor.Infrastructure.Logging;

public class ActivityFileLoggerProvider : ILoggerProvider
{
    private readonly string _componentName;
    private readonly BlockingCollection<string> _messageQueue = new(new ConcurrentQueue<string>(), 5000);
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _writeThread;

    public ActivityFileLoggerProvider(string componentName)
    {
        _componentName = componentName;
        _writeThread = new Thread(ProcessLogQueue)
        {
            IsBackground = true,
            Name = $"{componentName}LogWriter"
        };
        _writeThread.Start();
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new ActivityFileLogger(_componentName, categoryName, this);
    }

    internal void EnqueueMessage(string message)
    {
        if (!_messageQueue.IsAddingCompleted)
        {
            try
            {
                _messageQueue.TryAdd(message, 50);
            }
            catch
            {
                // drop log message under extreme backpressure
            }
        }
    }

    private void ProcessLogQueue()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (_messageQueue.TryTake(out var message, 1000, _cts.Token))
                {
                    var filePath = ActivityMonitorPaths.GetLogFilePath(_componentName, DateTime.UtcNow);
                    File.AppendAllText(filePath, message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Never crash the background writer
            }
        }
    }

    public void Dispose()
    {
        _messageQueue.CompleteAdding();
        _cts.Cancel();
        try
        {
            _writeThread.Join(500);
        }
        catch { }
        _cts.Dispose();
        _messageQueue.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class ActivityFileLogger : ILogger
{
    private readonly string _componentName;
    private readonly string _categoryName;
    private readonly ActivityFileLoggerProvider _provider;

    public ActivityFileLogger(string componentName, string categoryName, ActivityFileLoggerProvider provider)
    {
        _componentName = componentName;
        _categoryName = categoryName;
        _provider = provider;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logLine = $"[{timestamp}] [{logLevel,-5}] [{_categoryName}] {message}";

        if (exception != null)
        {
            logLine += Environment.NewLine + exception.ToString();
        }

        _provider.EnqueueMessage(logLine);
    }
}

public static class ActivityFileLoggerExtensions
{
    public static ILoggingBuilder AddActivityFileLogger(this ILoggingBuilder builder, string componentName)
    {
        builder.AddProvider(new ActivityFileLoggerProvider(componentName));
        return builder;
    }
}
