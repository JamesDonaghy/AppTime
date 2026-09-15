using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AppTime
{
    /// A single application tile in the library grid. Owner-drawn (rather than built
    /// from child Label controls) so the rounded card shape, border and hover state can
    /// all be handled in one place. A single click opens the app's details view;
    /// right-click gives "Edit..." and "Remove from Library" options. Usage time is
    /// shown above a Start/Running/Stop button rather than relying on double-click
    /// (which doesn't play well with the single-click-for-details behaviour above).
    public class AppCard : Panel
    {
        private const int CornerRadius = 10;
        private const int ContentPadding = 12;
        private const int ActionButtonHeight = 24;
        private const int UsageTextHeight = 16;
        private const int UsageButtonGap = 4;

        private bool isHovered;
        private bool isRunning;
        private bool isActionButtonHovered;

        private readonly Button actionButton;

        public AppEntry App { get; }

        // Set by MainForm after each process-detection pass. Only updates the button
        // and repaints when the value actually changes, so a timer ticking every few
        // seconds doesn't force a redraw of every card each time.
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
                UpdateActionButtonAppearance();
                Invalidate();
            }
        }

        // Raised when the action button is clicked while not running. MainForm owns
        // the actual Process.Start call - this control only knows how to ask for it.
        public event EventHandler? LaunchRequested;

        // Raised when the action button is clicked while running (shown as "Stop" on
        // hover). MainForm owns actually ending the process.
        public event EventHandler? StopRequested;

        // Raised when "Remove from Library" is chosen from the right-click menu.
        // MainForm owns the confirmation prompt and the actual removal.
        public event EventHandler? RemoveRequested;

        // Raised when "Edit..." is chosen from the right-click menu. MainForm owns
        // showing the edit dialog and applying the result.
        public event EventHandler? EditRequested;

        // Raised on a single click - MainForm owns navigating to the app's details
        // view. Clicks on the action button don't bubble up to this (child control
        // clicks are separate from the parent Panel's own Click event), so this only
        // fires for clicks elsewhere on the card.
        public event EventHandler? DetailsRequested;

        public AppCard(AppEntry app)
        {
            App = app;

            Margin = new Padding(0, 0, 16, 16);
            Cursor = Cursors.Hand;

            // Owner-drawn controls flicker without this - same reasoning as the form's
            // DoubleBuffered flag.
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            MouseEnter += (_, _) => { isHovered = true; Invalidate(); };
            MouseLeave += (_, _) => TryClearCardHover();

            actionButton = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Font = AppTheme.SmallText,
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            actionButton.FlatAppearance.BorderSize = 0;
            actionButton.Click += (_, _) =>
            {
                if (isRunning)
                {
                    StopRequested?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    LaunchRequested?.Invoke(this, EventArgs.Empty);
                }
            };
            actionButton.MouseEnter += (_, _) =>
            {
                isActionButtonHovered = true;
                UpdateActionButtonAppearance();

                // The button is a separate child window, so moving onto it fires this
                // Panel's own MouseLeave even though the cursor is still visually
                // within the card - keep the card's hover highlight on to match.
                isHovered = true;
                Invalidate();
            };
            actionButton.MouseLeave += (_, _) =>
            {
                isActionButtonHovered = false;
                UpdateActionButtonAppearance();
                TryClearCardHover();
            };
            Controls.Add(actionButton);
            UpdateActionButtonAppearance();

            // Size is set after actionButton exists - assigning Size triggers OnResize
            // (via SetBoundsCore/UpdateBounds), which calls PositionActionButton() and
            // would otherwise hit a null reference on actionButton if it ran first.
            Size = new Size(168, 118);

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

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            DetailsRequested?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PositionActionButton();
        }

        private void PositionActionButton()
        {
            actionButton.SetBounds(
                ContentPadding,
                Height - ContentPadding - ActionButtonHeight,
                Math.Max(0, Width - ContentPadding * 2),
                ActionButtonHeight);
        }

        // Both this Panel's own MouseLeave and the action button's MouseLeave fire in
        // cases where the cursor hasn't actually left the card (see comments where
        // these are wired up) - only clear the hover highlight if it genuinely has.
        private void TryClearCardHover()
        {
            if (ClientRectangle.Contains(PointToClient(Cursor.Position)))
            {
                return;
            }

            isHovered = false;
            Invalidate();
        }

        private void UpdateActionButtonAppearance()
        {
            if (!isRunning)
            {
                actionButton.Text = "Start";
                actionButton.BackColor = AppTheme.Success;
                return;
            }

            if (isActionButtonHovered)
            {
                actionButton.Text = "Stop";
                actionButton.BackColor = AppTheme.Danger;
            }
            else
            {
                actionButton.Text = "Running";
                actionButton.BackColor = AppTheme.Accent;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

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

            var contentRect = new Rectangle(ContentPadding, ContentPadding, Width - ContentPadding * 2, Height - ContentPadding * 2);

            // App icon, top-left. Extracted from the executable (or a letter tile fallback).
            const int iconSize = 28;
            var icon = AppIconCache.GetIcon(App, iconSize);
            g.DrawImage(icon, contentRect.Left, contentRect.Top, iconSize, iconSize);

            // Category sits to the right of the icon so the top row stays compact.
            var categoryRect = new Rectangle(
                contentRect.Left + iconSize + 8,
                contentRect.Top,
                Math.Max(0, contentRect.Width - iconSize - 8 - 12),
                iconSize);
            TextRenderer.DrawText(g, App.Category, AppTheme.SmallText, categoryRect, AppTheme.TextSecondary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            // Application name below the icon row.
            var nameRect = new Rectangle(contentRect.Left, contentRect.Top + iconSize + 6, contentRect.Width, 36);
            TextRenderer.DrawText(g, App.Name, AppTheme.CardTitle, nameRect, AppTheme.TextPrimary,
                TextFormatFlags.Top | TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            // Small dot, top-right - whether the app's process is currently running.
            // Paired with the action button's own "Running" state below, rather than
            // relying on colour alone.
            if (isRunning)
            {
                const int dotSize = 8;
                var dotRect = new Rectangle(contentRect.Right - dotSize, contentRect.Top + 1, dotSize, dotSize);
                using var dotBrush = new SolidBrush(AppTheme.Success);
                g.FillEllipse(dotBrush, dotRect);
            }

            // Usage time, directly above the action button.
            var usageRect = new Rectangle(
                contentRect.Left,
                Height - ContentPadding - ActionButtonHeight - UsageButtonGap - UsageTextHeight,
                contentRect.Width,
                UsageTextHeight);
            TextRenderer.DrawText(g, FormatUsage(App.TotalUsageTime), AppTheme.SmallText, usageRect, AppTheme.TextSecondary,
                TextFormatFlags.Bottom | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }

        private static string FormatUsage(TimeSpan usage)
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