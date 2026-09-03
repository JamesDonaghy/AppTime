using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AppTime
{
    /// A single application tile in the library grid. Owner-drawn (rather than built
    /// from child Label controls) so the rounded card shape, border and hover state can
    /// all be handled in one place. Double-click launches the application; there's no
    /// separate Launch button yet, so the hover state doubles as the affordance hint.
    public class AppCard : Panel
    {
        private const int CornerRadius = 10;

        private bool isHovered;

        public AppEntry App { get; }

        // Raised on double-click. MainForm owns the actual Process.Start call - this
        // control only knows how to ask for it.
        public event EventHandler? LaunchRequested;

        public AppCard(AppEntry app)
        {
            App = app;

            Size = new Size(168, 118);
            Margin = new Padding(0, 0, 16, 16);
            Cursor = Cursors.Hand;

            // Owner-drawn controls flicker without this - same reasoning as the form's
            // DoubleBuffered flag.
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            MouseEnter += (_, _) => { isHovered = true; Invalidate(); };
            MouseLeave += (_, _) => { isHovered = false; Invalidate(); };
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            LaunchRequested?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedRect(bounds, CornerRadius);

            var backColor = isHovered ? AppTheme.CardHoverBackground : AppTheme.CardBackground;
            using (var backBrush = new SolidBrush(backColor))
            {
                g.FillPath(backBrush, path);
            }

            var borderColor = isHovered ? AppTheme.Accent : AppTheme.Border;
            using (var borderPen = new Pen(borderColor, 1))
            {
                g.DrawPath(borderPen, path);
            }

            var contentPadding = 12;
            var contentRect = new Rectangle(contentPadding, contentPadding, Width - contentPadding * 2, Height - contentPadding * 2);

            // Category, top-left - small and muted so the name reads first.
            TextRenderer.DrawText(g, App.Category, AppTheme.SmallText, contentRect, AppTheme.TextSecondary,
                TextFormatFlags.Top | TextFormatFlags.Left | TextFormatFlags.NoPadding);

            // Application name, roughly centred vertically in the middle of the card.
            var nameRect = new Rectangle(contentRect.Left, contentRect.Top + 22, contentRect.Width, 40);
            TextRenderer.DrawText(g, App.Name, AppTheme.CardTitle, nameRect, AppTheme.TextPrimary,
                TextFormatFlags.Top | TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

            // Bottom-left: usage time normally, a launch hint while hovered - keeps the
            // double-click affordance discoverable without adding a real button.
            var bottomText = isHovered ? "Double-click to launch" : FormatUsage(App.TotalUsageTime);
            var bottomColor = isHovered ? AppTheme.Accent : AppTheme.TextSecondary;
            var bottomRect = new Rectangle(contentRect.Left, contentRect.Bottom - 16, contentRect.Width, 16);
            TextRenderer.DrawText(g, bottomText, AppTheme.SmallText, bottomRect, bottomColor,
                TextFormatFlags.Bottom | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }

        private static string FormatUsage(System.TimeSpan usage)
        {
            if (usage.TotalMinutes < 1)
            {
                return "No usage yet";
            }

            var hours = (int)usage.TotalHours;
            var minutes = usage.Minutes;
            return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();

            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }
    }
}