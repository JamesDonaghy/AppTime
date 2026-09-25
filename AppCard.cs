using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AppTime
{
    /// A single application tile in the library grid. Owner-drawn (rather than built
    /// from child Label controls) so the rounded card shape, border and hover state can
    /// all be handled in one place. A single click opens the app's details view;
    /// right-click gives "Edit..." and "Remove from Library" options.
    ///
    /// Layout matches the library mock: category + running status on the top row,
    /// name and usage below, and a full-width rounded Start / Stop action button.
    public class AppCard : Panel
    {
        private const int CornerRadius = 12;
        private const int ButtonCornerRadius = 8;
        private const int ContentPadding = 12;
        private const int ActionButtonHeight = 30;
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

        public event EventHandler? LaunchRequested;
        public event EventHandler? StopRequested;
        public event EventHandler? RemoveRequested;
        public event EventHandler? EditRequested;
        public event EventHandler? DetailsRequested;

        public AppCard(AppEntry app)
        {
            App = app;

            // Keep footprint tight enough for 5 columns when a scrollbar is present.
            Margin = new Padding(0, 0, 10, 10);
            Cursor = Cursors.Hand;

            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.OptimizedDoubleBuffer,
                true);

            MouseEnter += (_, _) => { isHovered = true; Invalidate(); };
            MouseLeave += (_, _) => TryClearCardHover();

            actionButton = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Font = new Font(AppTheme.Base, FontStyle.Bold),
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
                isHovered = true;
                Invalidate();
            };
            actionButton.MouseLeave += (_, _) =>
            {
                isActionButtonHovered = false;
                UpdateActionButtonAppearance();
                TryClearCardHover();
            };
            actionButton.Resize += (_, _) => ApplyButtonRoundedRegion();
            Controls.Add(actionButton);
            UpdateActionButtonAppearance();

            // Sized for 5 columns; tall enough for icon, name, category, usage, button.
            Size = new Size(188, 142);

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
            ApplyButtonRoundedRegion();
        }

        private void ApplyButtonRoundedRegion()
        {
            if (actionButton.Width <= 0 || actionButton.Height <= 0)
            {
                return;
            }

            using var path = RoundedRect(
                new Rectangle(0, 0, actionButton.Width, actionButton.Height),
                ButtonCornerRadius);
            actionButton.Region?.Dispose();
            actionButton.Region = new Region(path);
        }

        private void TryClearCardHover()
        {
            if (ClientRectangle.Contains(PointToClient(Cursor.Position)))
            {
                return;
            }

            isHovered = false;
            Invalidate();
        }

        // Status text lives on the card; the button is only Start (idle) or Stop (running).
        private void UpdateActionButtonAppearance()
        {
            if (!isRunning)
            {
                actionButton.Text = "Start";
                // Green Start with a brighter green hover (mirrors red Stop hover).
                actionButton.BackColor = isActionButtonHovered
                    ? Color.FromArgb(0x1F, 0xB8, 0x6A)
                    : AppTheme.Success;
                return;
            }

            actionButton.Text = "Stop";
            actionButton.BackColor = isActionButtonHovered
                ? AppTheme.Danger
                : AppTheme.Accent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

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

            var contentRect = new Rectangle(
                ContentPadding,
                ContentPadding,
                Width - ContentPadding * 2,
                Height - ContentPadding * 2);

            // App icon, top-left.
            const int iconSize = 28;
            var icon = AppIconCache.GetIcon(App, iconSize);
            g.DrawImage(icon, contentRect.Left, contentRect.Top, iconSize, iconSize);

            // Running / Not running indicator, top-right.
            DrawRunningStatus(g, contentRect);

            // Stack: name → category → usage, then the action button below.
            var nameTop = contentRect.Top + iconSize + 6;
            var nameRect = new Rectangle(contentRect.Left, nameTop, contentRect.Width, 18);
            TextRenderer.DrawText(
                g,
                App.Name,
                AppTheme.CardTitle,
                nameRect,
                AppTheme.TextPrimary,
                TextFormatFlags.Top
                | TextFormatFlags.Left
                | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPadding);

            var category = string.IsNullOrWhiteSpace(App.Category) ? "Other" : App.Category;
            var categoryRect = new Rectangle(contentRect.Left, nameRect.Bottom + 1, contentRect.Width, 15);
            TextRenderer.DrawText(
                g,
                category,
                AppTheme.SmallText,
                categoryRect,
                AppTheme.TextSecondary,
                TextFormatFlags.Top
                | TextFormatFlags.Left
                | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPadding);

            var usageTop = categoryRect.Bottom + 2;
            var usageRect = new Rectangle(contentRect.Left, usageTop, contentRect.Width, UsageTextHeight);
            TextRenderer.DrawText(
                g,
                FormatUsage(App.TotalUsageTime),
                AppTheme.SmallText,
                usageRect,
                AppTheme.TextSecondary,
                TextFormatFlags.Top | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }

        private void DrawRunningStatus(Graphics g, Rectangle contentRect)
        {
            const int dotSize = 7;
            var statusText = isRunning ? "Running" : "Not running";
            var statusColor = isRunning ? AppTheme.Success : AppTheme.TextSecondary;
            var font = AppTheme.SmallText;

            var textSize = TextRenderer.MeasureText(
                statusText,
                font,
                new Size(200, 20),
                TextFormatFlags.NoPadding);

            var right = contentRect.Right;
            var textWidth = Math.Min(textSize.Width, contentRect.Width / 2 + 20);
            var textRect = new Rectangle(
                right - textWidth,
                contentRect.Top,
                textWidth,
                20);

            // Dot sits just left of the status label.
            if (isRunning)
            {
                var dotX = textRect.Left - dotSize - 5;
                var dotY = contentRect.Top + (20 - dotSize) / 2;
                if (dotX >= contentRect.Left)
                {
                    using var dotBrush = new SolidBrush(AppTheme.Success);
                    g.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);
                }
            }
            else
            {
                // Subtle grey dot for the idle state so the layout stays balanced.
                var dotX = textRect.Left - dotSize - 5;
                var dotY = contentRect.Top + (20 - dotSize) / 2;
                if (dotX >= contentRect.Left)
                {
                    using var dotBrush = new SolidBrush(Color.FromArgb(0xC0, 0xC5, 0xCE));
                    g.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);
                }
            }

            TextRenderer.DrawText(
                g,
                statusText,
                font,
                textRect,
                statusColor,
                TextFormatFlags.VerticalCenter
                | TextFormatFlags.Right
                | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPadding);
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

            if (radius <= 0 || bounds.Width < diameter || bounds.Height < diameter)
            {
                path.AddRectangle(bounds);
                path.CloseFigure();
                return path;
            }

            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }
    }
}