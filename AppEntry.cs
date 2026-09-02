using System;

namespace AppTime
{
    /// Represents a single desktop application tracked in the AppTime library.
    ///
    /// Named "AppEntry" rather than "Application" to avoid colliding with
    /// System.Windows.Forms.Application, which is in scope throughout this project.
    public class AppEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty;

        public string ExecutablePath { get; set; } = string.Empty;

        // Free-text for now (e.g. "Creative", "Development"). A fixed set/enum can
        // replace this later if categories need to be managed rather than just shown.
        public string Category { get; set; } = string.Empty;

        // Running total of tracked active usage. Not populated by anything yet - time
        // tracking comes in a later stage. Sample data seeds this for the UI to show.
        public TimeSpan TotalUsageTime { get; set; } = TimeSpan.Zero;

        // Null until the app has actually been launched/tracked at least once.
        public DateTime? LastUsed { get; set; }
    }
}