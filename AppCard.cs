using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AppTime
{
    /// A single application tile in the library grid. Owner-drawn (rather than built
    /// from child Label controls) so the rounded card shape, border and hover state can
    /// all be handled in one place. Double-click launches the application; right-click
    /// gives "Edit..." and "Remove from Library" options. A small dot and status text
    /// show whether the app's process is currently detected running.
    public class AppCard : Panel
    {
        private const int CornerRadius = 10;

        private bool isHovered;
        private bool isRunning;

        public AppEntry App { get; }

        // Set by MainForm after each process-detection pass. Only repaints when the
        // value actually changes, so a timer ticking every few seconds doesn't force a
        // redraw of every card each time.
        public bool IsRunning
        {
            get => isRunning;
            set
            {
                if (isRunning == value)
                {
                    return;
                }

                isRunning = value;
                Invalidate();
            }
        }

        // Raised on double-click. MainForm owns the actual Process.Start call - this
        // control only knows how to ask for it.
        public event EventHandler? LaunchRequested;

        // Raised when "Remove from Library" is chosen from the right-click menu.
        // MainForm owns the confirmation prompt and the actual removal.
        public event EventHandler? RemoveRequested;

        // Raised when "Edit..." is chosen from the right-click menu. MainForm owns
        // showing the edit dialog and applying the result.
        public event EventHandler? EditRequested;

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

            var editItem = new ToolStripMenuItem("Edit...");
            editItem.Click += (_, _) => EditRequested?.Invoke(this, EventArgs.Empty);

            var removeItem = new ToolStripMenuItem("Remove from Library");
            removeItem.Click += (_, _) => RemoveRequested?.Invoke(this, EventArgs.Empty);

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add(editItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(removeItem);
            ContextMenuStrip = contextMenu;
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

            // Small dot, top-right - whether the app's process is currently running.
            // Paired with the "Running" text below rather than relying on colour alone.
            if (isRunning)
            {
                const int dotSize = 8;
                var dotRect = new Rectangle(contentRect.Right - dotSize, contentRect.Top + 1, dotSize, dotSize);
                using var dotBrush = new SolidBrush(AppTheme.Success);
                g.FillEllipse(dotBrush, dotRect);
            }

            // Bottom-left: usage time normally, "Running" if the process is currently
            // detected, or a launch hint while hovered - hover always wins, since
            // that's the user actively interacting with the card right now.
            string bottomText;
            Color bottomColor;
            if (isHovered)
            {
                bottomText = "Double-click to launch";
                bottomColor = AppTheme.Accent;
            }
            else if (isRunning)
            {
                bottomText = "Running";
                bottomColor = AppTheme.Success;
            }
            else
            {
                bottomText = FormatUsage(App.TotalUsageTime);
                bottomColor = AppTheme.TextSecondary;
            }

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