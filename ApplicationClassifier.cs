using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AppTime
{
    /// Lightweight classifier for Suggested Applications.
    /// Rejects runtimes, plugins, helpers, drivers, and similar non-apps.
    /// When uncertain, returns Unknown (not suggested).
    public static class ApplicationClassifier
    {
        public sealed class Result
        {
            public ApplicationKind Kind { get; init; }
            public string DisplayName { get; init; } = string.Empty;
            public string SuggestedCategory { get; init; } = "Other";
            public bool IsSuggestable => Kind == ApplicationKind.UserFacing;
        }

        private static readonly string[] NonAppPathMarkers =
        {
            @"\windows\system32\",
            @"\windows\syswow64\",
            @"\windows\winsxs\",
            @"\windows\systemapps\",
            @"\windows\installer\",
            @"\driverstore\",
            @"\redistributable\",
            @"\vcredist\",
            @"\dotnet\",
            @"\microsoft.net\",
            @"\runtime\",
            @"\runtimes\",
            @"\framework\",
            @"\package cache\",
            @"\windows kits\",
            @"\msbuild\",
            @"\node_modules\"
        };

        private static readonly string[] NonAppTokens =
        {
            "redist", "redistributable", "runtime", "framework", "sdk",
            "plugin", "helper", "service", "updater", "crash", "crashpad",
            "broker", "host", "agent", "daemon", "driver", "overlay",
            "cefsharp", "browser subprocess", "gpu-process", "renderer",
            "chrome_proxy", "installer", "setup", "uninstall",
            "vcredist", "vc_redist", "dotnet", "msvcp", "vcruntime",
            "gameinput", "gameinputredist", "deviceplugin", "icuedeviceplugin",
            "asus_framework", "ambientlight", "pcbroker", "sessionhost",
            "telemetry", "watchdog", "werfault", "dllhost", "rundll",
            "notification_helper", "elevation_service", "steamwebhelper",
            "nvidia overlay", "servicehub", "msbuild", "vbcscompiler"
        };

        public static Result ClassifyExecutable(string executablePath, string? processName = null)
        {
            processName ??= Path.GetFileNameWithoutExtension(executablePath) ?? string.Empty;

            string? product = null;
            string? description = null;
            try
            {
                if (File.Exists(executablePath))
                {
                    var info = FileVersionInfo.GetVersionInfo(executablePath);
                    product = info.ProductName?.Trim();
                    description = info.FileDescription?.Trim();
                }
            }
            catch
            {
                // Version info is best-effort.
            }

            var path = executablePath ?? string.Empty;
            var haystack = string.Join(" ", path, processName, product, description).ToLowerInvariant();

            if (NonAppPathMarkers.Any(m => path.Contains(m, StringComparison.OrdinalIgnoreCase)))
            {
                return Reject(processName, product, description);
            }

            if (NonAppTokens.Any(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase)))
            {
                return Reject(processName, product, description);
            }

            var display = PreferDisplayName(product, description, processName);
            if (IsTechnicalName(display) || IsTechnicalName(processName))
            {
                return new Result
                {
                    Kind = ApplicationKind.Unknown,
                    DisplayName = display,
                    SuggestedCategory = "Other"
                };
            }

            // Require a readable product name (not just a bare process id).
            if (string.IsNullOrWhiteSpace(product) || IsTechnicalName(product))
            {
                return new Result
                {
                    Kind = ApplicationKind.Unknown,
                    DisplayName = display,
                    SuggestedCategory = "Other"
                };
            }

            return new Result
            {
                Kind = ApplicationKind.UserFacing,
                DisplayName = display,
                SuggestedCategory = GuessCategory(haystack)
            };
        }

        private static Result Reject(string processName, string? product, string? description)
        {
            return new Result
            {
                Kind = ApplicationKind.NonApp,
                DisplayName = PreferDisplayName(product, description, processName),
                SuggestedCategory = "Other"
            };
        }

        private static string PreferDisplayName(string? product, string? description, string processName)
        {
            if (!string.IsNullOrWhiteSpace(product) && !IsTechnicalName(product))
            {
                return product.Trim();
            }

            if (!string.IsNullOrWhiteSpace(description) && !IsTechnicalName(description))
            {
                return description.Trim();
            }

            return processName.Trim();
        }

        private static bool IsTechnicalName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return true;
            }

            var n = name.ToLowerInvariant();
            if (NonAppTokens.Any(t => n.Contains(t, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // Dotted module names (CefSharp.BrowserSubprocess) or snake_case tech ids.
            if (!name.Contains(' ') && (name.Contains('.') || name.Contains('_')))
            {
                return true;
            }

            return false;
        }

        private static string GuessCategory(string haystack)
        {
            if (Contains(haystack, "code", "studio", "git", "docker", "jetbrains", "rider", "intellij"))
            {
                return "Development";
            }

            if (Contains(haystack, "photoshop", "premiere", "blender", "figma", "illustrator",
                    "after effects", "resolve", "canva", "obsidian", "notion"))
            {
                return "Creative";
            }

            if (Contains(haystack, "steam", "epic", "game", "minecraft", "riot", "xbox"))
            {
                return "Games";
            }

            return "Utilities";
        }

        private static bool Contains(string haystack, params string[] tokens)
        {
            return tokens.Any(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase));
        }
    }
}