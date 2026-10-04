using System;
using System.IO;

namespace ActivityMonitor.Core.Options;

public static class ActivityMonitorPaths
{
    private static string? _customRootDirectory;

    public static void SetCustomRootDirectory(string? root)
    {
        _customRootDirectory = root;
    }

    public static string RootDirectory =>
        _customRootDirectory ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ActivityMonitor");

    public static string DataDirectory => Path.Combine(RootDirectory, "Data");
    public static string DatabasePath => Path.Combine(DataDirectory, "activity.db");
    public static string RecordingsDirectory => Path.Combine(RootDirectory, "Recordings");
    public static string ReportsDirectory => Path.Combine(RootDirectory, "Reports");
    public static string LogsDirectory => Path.Combine(RootDirectory, "Logs");

    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(RecordingsDirectory);
        Directory.CreateDirectory(ReportsDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    public static string GetRecordingDirectory(string machineId, DateTime timestamp)
    {
        return Path.Combine(
            RecordingsDirectory,
            Sanitize(machineId),
            timestamp.ToString("yyyy"),
            timestamp.ToString("MM"),
            timestamp.ToString("dd"),
            timestamp.ToString("HH")
        );
    }

    public static string GetRecordingFilePath(string machineId, DateTime timestamp, string? fileName = null)
    {
        var dir = GetRecordingDirectory(machineId, timestamp);
        Directory.CreateDirectory(dir);
        var actualFileName = fileName ?? $"{timestamp:yyyy-MM-dd_HH-mm-ss}.mp4";
        return Path.Combine(dir, actualFileName);
    }

    public static string GetReportDirectory(string machineId, DateTime date)
    {
        return Path.Combine(
            ReportsDirectory,
            Sanitize(machineId),
            date.ToString("yyyy"),
            date.ToString("MM")
        );
    }

    public static string GetReportFilePath(string machineId, DateTime date, string extension)
    {
        var dir = GetReportDirectory(machineId, date);
        Directory.CreateDirectory(dir);
        var ext = extension.TrimStart('.');
        return Path.Combine(dir, $"{date:yyyy-MM-dd}.{ext}");
    }

    public static string GetLogFilePath(string componentName, DateTime date)
    {
        Directory.CreateDirectory(LogsDirectory);
        return Path.Combine(LogsDirectory, $"{componentName.ToLowerInvariant()}-{date:yyyy-MM-dd}.log");
    }

    private static string Sanitize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "DEFAULT";

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            input = input.Replace(c, '_');
        }
        return input;
    }
}
