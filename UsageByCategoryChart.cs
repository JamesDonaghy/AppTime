using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AppTime
{
    /// Donut chart + right-hand legend for category breakdown on the Insights page.
    /// Matches the visual style of WeeklyUsageChart (owner-drawn, anti-aliased).
    public class UsageByCategoryChart : Panel
    {
        private List<(string Category, TimeSpan Duration, double Percent, Color Color)> data = new();
        private TimeSpan total = TimeSpan.Zero;

        // Preferred category order so the chart matches the design screenshot.
        private static readonly string[] PreferredOrder =
        {
            "Development", "Creative", "Utilities", "Games"
        };

        public UsageByCategoryChart()
        {
            BackColor = AppTheme.CardBackground;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.SupportsTransparentBackColor,
                true);
        }

        public void SetData(IEnumerable<(string Category, TimeSpan Duration)> categories)
        {
            var list = categories
                .Where(c => c.Duration > TimeSpan.Zero)
                .ToList();

            total = TimeSpan.FromTicks(list.Sum(c => c.Duration.Ticks));

            // Stable order: known categories first, then anything else alphabetically.
            data = list
                .OrderBy(c =>
                {
                    var idx = Array.IndexOf(PreferredOrder, c.Category);
                    return idx >= 0 ? idx : 100;
                })
                .ThenBy(c => c.Category)
                .Select(c =>
                {
                    var percent = total.Ticks > 0
                        ? c.Duration.Ticks * 100.0 / total.Ticks
                        : 0;
                    return (c.Category, c.Duration, percent, AppTheme.CategoryColor(c.Category));
                })
                .ToList();

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (data.Count == 0 || total <= TimeSpan.Zero)
            {
                using var brush = new SolidBrush(AppTheme.TextSecondary);
                var message = "No usage tracked yet.";
                var size = g.MeasureString(message, AppTheme.Base);
                g.DrawString(message, AppTheme.Base, brush,
                    (Width - size.Width) / 2, (Height - size.Height) / 2);
                return;
            }

            // Layout: donut on the left, legend on the right.
            var padding = 8;
            var legendWidth = Math.Max(140, (int)(Width * 0.48));
            var donutAreaWidth = Width - legendWidth - padding * 2;
            var donutAreaHeight = Height - padding * 2;

            var outerDiameter = Math.Min(donutAreaWidth, donutAreaHeight) - 8;
            outerDiameter = Math.Max(80, outerDiameter);
            var outerRadius = outerDiameter / 2f;
            var innerRadius = outerRadius * 0.62f;

            var centerX = padding + donutAreaWidth / 2f;
            var centerY = Height / 2f;

            var outerRect = new RectangleF(
                centerX - outerRadius,
                centerY - outerRadius,
                outerDiameter,
                outerDiameter);

            // Draw donut slices.
            float startAngle = -90f; // start at top
            foreach (var slice in data)
            {
                var sweep = (float)(slice.Percent / 100.0 * 360.0);
                if (sweep <= 0.01f)
                {
                    continue;
                }

                using var path = new GraphicsPath();
                path.AddArc(outerRect, startAngle, sweep);

                var innerRect = new RectangleF(
                    centerX - innerRadius,
                    centerY - innerRadius,
                    innerRadius * 2,
                    innerRadius * 2);

                // Reverse arc for the inner hole so the path closes correctly.
                path.AddArc(innerRect, startAngle + sweep, -sweep);
                path.CloseFigure();

                using var brush = new SolidBrush(slice.Color);
                g.FillPath(brush, path);

                startAngle += sweep;
            }

            // Centre labels: total duration + "Total".
            var totalText = FormatDuration(total);
            using (var totalFont = new Font("Segoe UI", 11f, FontStyle.Bold))
            using (var totalBrush = new SolidBrush(AppTheme.TextPrimary))
            {
                var totalSize = g.MeasureString(totalText, totalFont);
                g.DrawString(
                    totalText,
                    totalFont,
                    totalBrush,
                    centerX - totalSize.Width / 2,
                    centerY - totalSize.Height / 2 - 6);
            }

            using (var labelBrush = new SolidBrush(AppTheme.TextSecondary))
            {
                var label = "Total";
                var labelSize = g.MeasureString(label, AppTheme.SmallText);
                g.DrawString(
                    label,
                    AppTheme.SmallText,
                    labelBrush,
                    centerX - labelSize.Width / 2,
                    centerY + 8);
            }

            // Legend on the right.
            DrawLegend(g, Width - legendWidth - padding, padding, legendWidth, Height - padding * 2);
        }

        private void DrawLegend(Graphics g, int left, int top, int width, int height)
        {
            if (data.Count == 0)
            {
                return;
            }

            const int rowHeight = 26;
            const int swatchSize = 8;
            var totalLegendHeight = data.Count * rowHeight;
            var startY = top + Math.Max(0, (height - totalLegendHeight) / 2);

            using var nameBrush = new SolidBrush(AppTheme.TextPrimary);
            using var metaBrush = new SolidBrush(AppTheme.TextSecondary);

            // Reserve space on the right for duration + percent so names don't collide.
            const int metaColumnWidth = 72;

            for (var i = 0; i < data.Count; i++)
            {
                var item = data[i];
                var y = startY + i * rowHeight;

                // Colour swatch (filled circle).
                using (var swatchBrush = new SolidBrush(item.Color))
                {
                    g.FillEllipse(swatchBrush, left, y + 5, swatchSize, swatchSize);
                }

                var textLeft = left + swatchSize + 8;
                var nameMaxWidth = Math.Max(40, width - metaColumnWidth - (textLeft - left));

                // Category name (trim if the card is very narrow).
                var name = item.Category;
                var nameSize = g.MeasureString(name, AppTheme.Base);
                if (nameSize.Width > nameMaxWidth)
                {
                    while (name.Length > 1 && g.MeasureString(name + "…", AppTheme.Base).Width > nameMaxWidth)
                    {
                        name = name[..^1];
                    }
                    name += "…";
                }
                g.DrawString(name, AppTheme.Base, nameBrush, textLeft, y);

                // Duration and percent, right-aligned in the meta column.
                var durationText = FormatDuration(item.Duration);
                var percentText = $"{item.Percent:0}%";

                var durationSize = g.MeasureString(durationText, AppTheme.SmallText);
                var percentSize = g.MeasureString(percentText, AppTheme.SmallText);

                var metaRight = left + width;
                g.DrawString(
                    percentText,
                    AppTheme.SmallText,
                    metaBrush,
                    metaRight - percentSize.Width,
                    y + 2);
                g.DrawString(
                    durationText,
                    AppTheme.SmallText,
                    metaBrush,
                    metaRight - percentSize.Width - 6 - durationSize.Width,
                    y + 2);
            }
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                return "0m";
            }

            if (duration.TotalMinutes < 1)
            {
                return "<1m";
            }

            var hours = (int)duration.TotalHours;
            var minutes = duration.Minutes;
            return hours > 0 ? $"{hours}h {minutes:D2}m" : $"{minutes}m";
        }
    }
}
