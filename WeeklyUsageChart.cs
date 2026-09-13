using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AppTime
{
    /// Stacked vertical bar chart for a week's usage - one bar per day (Mon–Sun),
    /// segments coloured by category, with a small legend on the right.
    public class WeeklyUsageChart : Panel
    {
        private const int ChartPadding = 12;
        private const int LeftAxisWidth = 32;
        private const int DayLabelAreaHeight = 20;
        private const int BarGap = 14;
        private const int LegendWidth = 110;

        private static readonly string[] PreferredOrder =
        {
            "Development", "Creative", "Utilities", "Games"
        };

        // One entry per day; Segments are category → duration for that day.
        private List<(string DayLabel, Dictionary<string, TimeSpan> Segments)> data = new();

        public WeeklyUsageChart()
        {
            BackColor = AppTheme.CardBackground;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void SetData(List<(string DayLabel, Dictionary<string, TimeSpan> Segments)> weekData)
        {
            data = weekData ?? new List<(string, Dictionary<string, TimeSpan>)>();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using var axisFont = new Font(AppTheme.SmallText, FontStyle.Regular);
            using var axisBrush = new SolidBrush(AppTheme.TextSecondary);

            if (data.Count == 0 || data.All(d => d.Segments.Values.All(t => t <= TimeSpan.Zero)))
            {
                var message = "No usage tracked this week yet.";
                var messageSize = g.MeasureString(message, AppTheme.Base);
                g.DrawString(message, AppTheme.Base, axisBrush,
                    (Width - messageSize.Width) / 2, (Height - messageSize.Height) / 2);
                return;
            }

            var chartLeft = ChartPadding + LeftAxisWidth;
            var chartTop = ChartPadding;
            var chartWidth = Width - chartLeft - ChartPadding - LegendWidth;
            var chartHeight = Height - chartTop - ChartPadding - DayLabelAreaHeight;
            if (chartWidth <= 0 || chartHeight <= 0)
            {
                return;
            }

            // Categories present this week, in preferred order then alphabetical.
            var categories = data
                .SelectMany(d => d.Segments.Where(s => s.Value > TimeSpan.Zero).Select(s => s.Key))
                .Distinct()
                .OrderBy(c =>
                {
                    var idx = Array.IndexOf(PreferredOrder, c);
                    return idx >= 0 ? idx : 100;
                })
                .ThenBy(c => c)
                .ToList();

            var dayTotals = data
                .Select(d => TimeSpan.FromTicks(d.Segments.Values.Sum(t => t.Ticks)))
                .ToList();
            var maxHours = Math.Max(1, (int)Math.Ceiling(dayTotals.Max(t => t.TotalHours)));

            // Grid + Y-axis labels
            using (var gridPen = new Pen(AppTheme.Border, 1))
            {
                for (var hour = 0; hour <= maxHours; hour++)
                {
                    var y = chartTop + chartHeight - (int)(chartHeight * (hour / (double)maxHours));
                    g.DrawLine(gridPen, chartLeft, y, chartLeft + chartWidth, y);

                    var label = hour == 0 ? "0" : $"{hour}h";
                    var labelSize = g.MeasureString(label, axisFont);
                    g.DrawString(label, axisFont, axisBrush,
                        chartLeft - labelSize.Width - 6, y - labelSize.Height / 2);
                }
            }

            var dayCount = data.Count;
            var totalGapWidth = BarGap * Math.Max(0, dayCount - 1);
            var barWidth = Math.Max(8, (chartWidth - totalGapWidth) / dayCount);
            // Slightly rounded look via a small radius, but keep simple fills.
            const int barRadius = 3;

            var x = chartLeft;
            for (var dayIndex = 0; dayIndex < data.Count; dayIndex++)
            {
                var (dayLabel, segments) = data[dayIndex];

                // Build ordered stack for this day (bottom → top).
                var stack = new List<(string Category, int Height)>();
                foreach (var category in categories)
                {
                    if (!segments.TryGetValue(category, out var duration) || duration <= TimeSpan.Zero)
                    {
                        continue;
                    }

                    var segmentHeight = Math.Max(1, (int)Math.Round(chartHeight * (duration.TotalHours / maxHours)));
                    stack.Add((category, segmentHeight));
                }

                // Draw from bottom up; round only the topmost segment.
                var currentBottom = chartTop + chartHeight;
                for (var s = 0; s < stack.Count; s++)
                {
                    var (category, segmentHeight) = stack[s];
                    var isTop = s == stack.Count - 1;
                    var segmentTop = currentBottom - segmentHeight;
                    var rect = new Rectangle(x, segmentTop, barWidth, segmentHeight);

                    using var brush = new SolidBrush(AppTheme.CategoryColor(category));
                    FillRoundedTop(g, brush, rect, barRadius, isTop);

                    currentBottom = segmentTop;
                }

                // Day label under the bar
                var dayLabelSize = g.MeasureString(dayLabel, axisFont);
                g.DrawString(dayLabel, axisFont, axisBrush,
                    x + (barWidth - dayLabelSize.Width) / 2f, chartTop + chartHeight + 4);

                x += barWidth + BarGap;
            }

            DrawLegend(g, categories, Width - LegendWidth - 4, chartTop, LegendWidth, chartHeight);
        }

        private static void FillRoundedTop(Graphics g, Brush brush, Rectangle rect, int radius, bool isTop)
        {
            if (rect.Height <= 0 || rect.Width <= 0)
            {
                return;
            }

            // Only round the top of the uppermost segment; lower segments stay square
            // so stacks sit flush against each other.
            if (!isTop || rect.Height < radius * 2)
            {
                g.FillRectangle(brush, rect);
                return;
            }

            using var path = new GraphicsPath();
            var d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddLine(rect.Right, rect.Bottom, rect.X, rect.Bottom);
            path.CloseFigure();
            g.FillPath(brush, path);
        }

        private void DrawLegend(Graphics g, List<string> categories, int left, int top, int width, int height)
        {
            if (categories.Count == 0)
            {
                return;
            }

            const int rowHeight = 22;
            const int swatchSize = 8;
            var totalHeight = categories.Count * rowHeight;
            var startY = top + Math.Max(0, (height - totalHeight) / 2);

            using var textBrush = new SolidBrush(AppTheme.TextSecondary);

            for (var i = 0; i < categories.Count; i++)
            {
                var category = categories[i];
                var y = startY + i * rowHeight;

                using (var swatchBrush = new SolidBrush(AppTheme.CategoryColor(category)))
                {
                    g.FillEllipse(swatchBrush, left, y + 4, swatchSize, swatchSize);
                }

                g.DrawString(category, AppTheme.SmallText, textBrush, left + swatchSize + 6, y);
            }
        }
    }
}
