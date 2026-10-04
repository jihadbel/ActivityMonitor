using System;
using System.IO;
using System.Text.RegularExpressions;
using ActivityMonitor.Core.Interfaces;

namespace ActivityMonitor.Infrastructure.ContextProviders;

public class DefaultContextProvider : IActivityContextProvider
{
    private static readonly Regex FilePathRegex = new(
        @"[a-zA-Z]:\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\r\n]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public int Order => 100;

    public bool CanHandle(string processName, IntPtr windowHandle) => true;

    public string? GetContext(string processName, IntPtr windowHandle, string windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle)) return null;

        // Try to identify file paths mentioned in the title (common in text editors, IDEs, etc.)
        var match = FilePathRegex.Match(windowTitle);
        if (match.Success)
        {
            var potentialPath = match.Value.Trim();
            if (File.Exists(potentialPath) || Directory.Exists(potentialPath))
            {
                return potentialPath;
            }
        }

        // For Visual Studio / VS Code, extracts active solution or file from window title
        if (processName.Contains("devenv", StringComparison.OrdinalIgnoreCase) ||
            processName.Contains("code", StringComparison.OrdinalIgnoreCase))
        {
            var parts = windowTitle.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0)
            {
                return parts[0];
            }
        }

        return null;
    }
}
