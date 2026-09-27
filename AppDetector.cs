using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AppTime
{
    /// Discovers running apps not yet in the library for the Suggested Applications
    /// strip. Uses <see cref="ApplicationClassifier"/> so technical components
    /// (runtimes, plugins, helpers, etc.) are not suggested.
    public static class AppDetector
    {
        private static readonly HashSet<string> IgnoredProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Idle", "System", "Registry", "smss", "csrss", "wininit", "services",
            "lsass", "svchost", "fontdrvhost", "dwm", "conhost", "RuntimeBroker",
            "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost",
            "TextInputHost", "sihost", "taskhostw", "explorer", "ApplicationFrameHost",
            "SystemSettings", "SecurityHealthService", "MsMpEng", "NisSrv",
            "AppTime", "dllhost", "rundll32", "regsvr32", "WerFault", "smartscreen"
        };

        public sealed class DetectedApp
        {
            public string Name { get; init; } = string.Empty;
            public string ExecutablePath { get; init; } = string.Empty;
            public string SuggestedCategory { get; init; } = "Other";
        }

        public static IReadOnlyList<DetectedApp> DetectSuggestions(
            IEnumerable<AppEntry> library,
            IEnumerable<string>? ignoredPaths = null,
            int maxCount = 6)
        {
            var knownPaths = new HashSet<string>(
                library
                    .Select(a => a.ExecutablePath)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(NormalizePath),
                StringComparer.OrdinalIgnoreCase);

            var ignored = new HashSet<string>(
                (ignoredPaths ?? Enumerable.Empty<string>())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(NormalizePath),
                StringComparer.OrdinalIgnoreCase);

            var knownNames = new HashSet<string>(
                library.Select(a => a.Name).Where(n => !string.IsNullOrWhiteSpace(n)),
                StringComparer.OrdinalIgnoreCase);

            var found = new Dictionary<string, DetectedApp>(StringComparer.OrdinalIgnoreCase);

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (IgnoredProcessNames.Contains(process.ProcessName))
                    {
                        continue;
                    }

                    string? path = null;
                    try
                    {
                        path = process.MainModule?.FileName;
                    }
                    catch
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(path)
                        || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        || !File.Exists(path))
                    {
                        continue;
                    }

                    var normalized = NormalizePath(path);
                    if (knownPaths.Contains(normalized)
                        || ignored.Contains(normalized)
                        || found.ContainsKey(normalized))
                    {
                        continue;
                    }

                    var classification = ApplicationClassifier.ClassifyExecutable(path, process.ProcessName);
                    if (!classification.IsSuggestable)
                    {
                        continue;
                    }

                    if (knownNames.Contains(classification.DisplayName))
                    {
                        continue;
                    }

                    found[normalized] = new DetectedApp
                    {
                        Name = classification.DisplayName,
                        ExecutablePath = path,
                        SuggestedCategory = classification.SuggestedCategory
                    };

                    if (found.Count >= maxCount * 3)
                    {
                        break;
                    }
                }
                catch
                {
                    // Ignore individual process failures.
                }
                finally
                {
                    try { process.Dispose(); } catch { /* ignore */ }
                }
            }

            return found.Values
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Take(maxCount)
                .ToList();
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path.Trim();
            }
        }
    }
}