using System.Drawing;

namespace AppTime
{
    /// Central place for AppTime's colour palette and typography. Controls should pull
    /// from here rather than hard-coding System.Drawing.Color/Font values directly, so
    /// the look stays consistent as more of the UI is built out and can be adjusted
    /// app-wide from one place.
    public static class AppTheme
    {
        // Base surface behind the whole window - plain white for now. A dark theme is
        // a likely future option, but white is the default while the UI is still
        // taking shape.
        public static readonly Color Background = Color.White;

        // Sidebar / top bar background - a very light grey so those regions read as
        // distinct panels against the white content area without needing a hard border.
        public static readonly Color PanelBackground = Color.FromArgb(0xF7, 0xF8, 0xFA);

        // Application card surface - white, same as the base background. Cards are
        // distinguished by their border rather than a fill colour difference.
        public static readonly Color CardBackground = Color.White;

        // Card background on hover - a light grey tint, just enough to signal
        // interactivity without being a jarring colour change.
        public static readonly Color CardHoverBackground = Color.FromArgb(0xF2, 0xF4, 0xF8);

        // Hairline border/divider colour - subtle on a light background.
        public static readonly Color Border = Color.FromArgb(0xE3, 0xE5, 0xEA);

        // Single accent colour, used sparingly (selection state, primary actions).
        public static readonly Color Accent = Color.FromArgb(0x2E, 0x8B, 0xF0);

        // Light tint of the accent colour - used for the selected sidebar item's
        // background, where the full accent colour would be too strong.
        public static readonly Color AccentSubtle = Color.FromArgb(0xEA, 0xF3, 0xFF);

        public static readonly Color TextPrimary = Color.FromArgb(0x20, 0x22, 0x2A);
        public static readonly Color TextSecondary = Color.FromArgb(0x6B, 0x72, 0x80);

        // Segoe UI is the standard modern Windows UI font and is present on every
        // supported Windows version - no new font files or dependencies needed.
        private const string FontFamily = "Segoe UI";

        public static Font Base => new Font(FontFamily, 9.5f);
        public static Font SmallText => new Font(FontFamily, 8.5f);
        public static Font Heading => new Font(FontFamily, 16f, FontStyle.Bold);
        public static Font SectionHeading => new Font(FontFamily, 10.5f, FontStyle.Bold);
        public static Font CardTitle => new Font(FontFamily, 10f, FontStyle.Bold);
    }
}