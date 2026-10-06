using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AppTime
{
    /// Horizontal 24-hour timeline (00:00–24:00) of today's focused sessions.
    /// Each segment is coloured by app category.
    public class DayTimelineChart : Panel
    {
        private const int PadLeft = 16;
        private const int PadRight = 16;
        private const int PadTop = 12;
        private const int AxisHeight = 22;
        private const int LegendHeight = 28;
        private const int TrackHeight = 28;

        private List<(DateTime Start, DateTime End, string Category)> segments = new();

        public DayTimelineChart()
        {
            BackColor = AppTheme.CardBackground;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.OptimizedDoubleBuffer,
                true);
        }

        public void SetSegments(List<(DateTime Start, DateTime End, string Category)> data)
        {
            segments = data ?? new List<(DateTime, DateTime, string)>();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var trackTop = PadTop + 8;
            var trackWidth = Math.Max(1, Width - PadLeft - PadRight);
            var trackRect = new Rectangle(PadLeft, trackTop, trackWidth, TrackHeight);

            // Track background
            using (var trackPath = RoundedRect(trackRect, 8))
            using (var trackBrush = new SolidBrush(Color.FromArgb(0xEE, 0xF1, 0xF5)))
            {
                g.FillPath(trackBrush, trackPath);
            }

            var dayStart = DateTime.Today;
            var dayEnd = dayStart.AddDays(1);
            var dayTicks = (dayEnd - dayStart).Ticks;

            foreach (var (start, end, category) in segments)
            {
                var clampedStart = start < dayStart ? dayStart : start;
                var clampedEnd = end > dayEnd ? dayEnd : end;
                if (clampedEnd <= clampedStart)
                {
                    continue;
                }

                var x1 = PadLeft + (int)((clampedStart - dayStart).Ticks / (double)dayTicks * trackWidth);
                var x2 = PadLeft + (int)((clampedEnd - dayStart).Ticks / (double)dayTicks * trackWidth);
                var w = Math.Max(3, x2 - x1);
                var segRect = new Rectangle(x1, trackTop, w, TrackHeight);

                using var path = RoundedRect(segRect, 6);
                using var brush = new SolidBrush(AppTheme.CategoryColor(category));
                g.FillPath(brush, path);
            }

            // Hour labels: 00:00, 06:00, 12:00, 18:00, 24:00
            var axisY = trackTop + TrackHeight + 6;
            using var axisFont = AppTheme.SmallText;
            using var axisBrush = new SolidBrush(AppTheme.TextSecondary);
            string[] labels = { "00:00", "06:00", "12:00", "18:00", "24:00" };
            double[] fractions = { 0, 0.25, 0.5, 0.75, 1.0 };

            for (var i = 0; i < labels.Length; i++)
            {
                var label = labels[i];
                var size = g.MeasureString(label, axisFont);
                var x = PadLeft + (int)(fractions[i] * trackWidth) - (int)(size.Width / 2);
                x = Math.Max(PadLeft, Math.Min(x, Width - PadRight - (int)size.Width));
                g.DrawString(label, axisFont, axisBrush, x, axisY);
            }

            // Category legend
            var legendY = axisY + AxisHeight;
            var legendItems = new (string Name, Color Color)[]
            {
                ("Development", AppTheme.CategoryDevelopment),
                ("Creative", AppTheme.CategoryCreative),
                ("Utilities", AppTheme.CategoryUtilities),
                ("Games", AppTheme.CategoryGames)
            };

            var legendX = PadLeft;
            using var legendFont = AppTheme.SmallText;
            foreach (var (name, color) in legendItems)
            {
                using (var swatch = new SolidBrush(color))
                {
                    g.FillEllipse(swatch, legendX, legendY + 4, 8, 8);
                }

                g.DrawString(name, legendFont, axisBrush, legendX + 12, legendY);
                var textWidth = g.MeasureString(name, legendFont).Width;
                legendX += 12 + (int)textWidth + 16;
            }

            if (segments.Count == 0)
            {
                const string empty = "No focused usage recorded today yet.";
                var emptySize = g.MeasureString(empty, AppTheme.Base);
                g.DrawString(
                    empty,
                    AppTheme.Base,
                    axisBrush,
                    PadLeft + (trackWidth - emptySize.Width) / 2,
                    trackTop + (TrackHeight - emptySize.Height) / 2);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var d = radius * 2;
            var path = new GraphicsPath();
            if (bounds.Width < d || bounds.Height < d)
            {
                path.AddRectangle(bounds);
                path.CloseFigure();
                return path;
            }

            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}