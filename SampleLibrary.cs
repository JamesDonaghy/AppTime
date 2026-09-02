using System;
using System.Collections.Generic;

namespace AppTime
{
    /// Placeholder library data used while there's no way to add real applications yet
    /// (no "Add Application" flow, no installed-app discovery). Gives the library view
    /// something realistic to display during development. This class - and everything
    /// that calls it - is expected to go away once applications can be added for real.
    public static class SampleLibrary
    {
        public static List<AppEntry> GetSampleApps()
        {
            var now = DateTime.Now;

            return new List<AppEntry>
            {
                new AppEntry
                {
                    Name = "Blender",
                    ExecutablePath = @"C:\Program Files\Blender Foundation\Blender\blender.exe",
                    Category = "Creative",
                    TotalUsageTime = TimeSpan.FromMinutes(18 * 60 + 52),
                    LastUsed = now.AddHours(-3),
                },
                new AppEntry
                {
                    Name = "Adobe Photoshop",
                    ExecutablePath = @"C:\Program Files\Adobe\Adobe Photoshop 2024\Photoshop.exe",
                    Category = "Creative",
                    TotalUsageTime = TimeSpan.FromMinutes(42 * 60 + 18),
                    LastUsed = now.AddDays(-1).AddHours(-2),
                },
                new AppEntry
                {
                    Name = "Adobe Premiere Pro",
                    ExecutablePath = @"C:\Program Files\Adobe\Adobe Premiere Pro 2024\Adobe Premiere Pro.exe",
                    Category = "Creative",
                    TotalUsageTime = TimeSpan.FromMinutes(31 * 60 + 4),
                    LastUsed = now.AddDays(-2),
                },
                new AppEntry
                {
                    Name = "Visual Studio Code",
                    ExecutablePath = @"C:\Users\User\AppData\Local\Programs\Microsoft VS Code\Code.exe",
                    Category = "Development",
                    TotalUsageTime = TimeSpan.FromMinutes(56 * 60 + 30),
                    LastUsed = now.AddHours(-1),
                },
                new AppEntry
                {
                    Name = "Visual Studio",
                    ExecutablePath = @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe",
                    Category = "Development",
                    TotalUsageTime = TimeSpan.FromMinutes(24 * 60 + 10),
                    LastUsed = now.AddDays(-4),
                }
            };
        }
    }
}