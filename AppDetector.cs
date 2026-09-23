using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AppTime
{
    /// Discovers desktop apps that are not yet in the library, primarily from
    /// processes that are currently running. Keeps results small and practical
    /// for a "Suggested" strip rather than a full installed-apps inventory.
    public static class AppDetector
    {
        private static readonly HashSet<string> IgnoredProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Idle", "System", "Registry", "smss", "csrss", "wininit", "services",
            "lsass", "svchost", "fontdrvhost", "dwm", "conhost", "RuntimeBroker",
            "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost",
            "TextInputHost", "sihost", "taskhostw", "explorer", "ApplicationFrameHost",
            "SystemSettings", "SecurityHealthService", "MsMpEng", "NisSrv",
            "AppTime", "devenv", "MSBuild", "VBCSCompiler", "ServiceHub",
            "PerfWatson2", "StandardCollector.Service"
        };

        private static readonly string[] SystemPathMarkers =
        {
            @"\Windows\System32\",
            @"\Windows\SysWOW64\",
            @"\Windows\WinSxS\",
            @"\Windows\SystemApps\",
            @"\WindowsApps\"
        };

        public sealed class DetectedApp
        {
            public string Name { get; init; } = string.Empty;
            public string ExecutablePath { get; init; } = string.Empty;
            public string SuggestedCategory { get; init; } = "Other";
        }

        /// Returns up to <paramref name="maxCount"/> apps that look addable and are
        /// not already present in <paramref name="library"/> (by executable path).
        public static IReadOnlyList<DetectedApp> DetectSuggestions(
            IEnumerable<AppEntry> library,
            int maxCount = 5)
        {
            var knownPaths = new HashSet<string>(
                library
                    .Select(a => a.ExecutablePath)
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
                        // Access denied for some system processes — skip.
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var normalized = NormalizePath(path);
                    if (knownPaths.Contains(normalized) || found.ContainsKey(normalized))
                    {
                        continue;
                    }

                    if (IsSystemPath(normalized))
                    {
                        continue;
                    }

                    var name = Path.GetFileNameWithoutExtension(path);
                    if (string.IsNullOrWhiteSpace(name) || knownNames.Contains(name))
                    {
                        continue;
                    }

                    found[normalized] = new DetectedApp
                    {
                        Name = name,
                        ExecutablePath = path,
                        SuggestedCategory = GuessCategory(path, name)
                    };

                    if (found.Count >= maxCount * 3)
                    {
                        // Collect a few extra then trim — process order is arbitrary.
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

        private static bool IsSystemPath(string path)
        {
            foreach (var marker in SystemPathMarkers)
            {
                if (path.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GuessCategory(string path, string name)
        {
            var haystack = $"{path} {name}".ToLowerInvariant();

            if (haystack.Contains("code") || haystack.Contains("studio") ||
                haystack.Contains("git") || haystack.Contains("docker") ||
                haystack.Contains("python") || haystack.Contains("node") ||
                haystack.Contains("jetbrains") || haystack.Contains("rider") ||
                haystack.Contains("intellij"))
            {
                return "Development";
            }

            if (haystack.Contains("photoshop") || haystack.Contains("premiere") ||
                haystack.Contains("blender") || haystack.Contains("figma") ||
                haystack.Contains("illustrator") || haystack.Contains("after effects") ||
                haystack.Contains("paint") || haystack.Contains("gimp") ||
                haystack.Contains("obsidian") || haystack.Contains("notion"))
            {
                return "Creative";
            }

            if (haystack.Contains("steam") || haystack.Contains("epic") ||
                haystack.Contains("game") || haystack.Contains("minecraft") ||
                haystack.Contains("riot") || haystack.Contains("battle.net") ||
                haystack.Contains("xbox"))
            {
                return "Games";
            }

            if (haystack.Contains("discord") || haystack.Contains("slack") ||
                haystack.Contains("teams") || haystack.Contains("zoom") ||
                haystack.Contains("chrome") || haystack.Contains("firefox") ||
                haystack.Contains("edge") || haystack.Contains("spotify") ||
                haystack.Contains("outlook") || haystack.Contains("mail"))
            {
                return "Utilities";
            }

            return "Other";
        }
    }
}