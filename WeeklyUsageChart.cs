using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AppTime
{
    /// A basic vertical bar chart for a week's worth of usage - one bar per day
    /// (Monday through Sunday), with simple hour gridlines/labels on the left. No
    /// per-category stacking or legend yet - single colour bars, kept simple rather
    /// than introducing a colour-per-category system before Usage by Category exists.
    public class WeeklyUsageChart : Panel
    {
        private const int ChartPadding = 12;
        private const int LeftAxisWidth = 32;
        private const int DayLabelAreaHeight = 20;
        private const int BarGap = 16;

        private List<(string DayLabel, TimeSpan Total)> data = new();

        public WeeklyUsageChart()
        {
            BackColor = AppTheme.CardBackground;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void SetData(List<(string DayLabel, TimeSpan Total)> weekData)
        {
            data = weekData;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using var axisFont = new Font(AppTheme.SmallText, FontStyle.Regular);
            using var axisBrush = new SolidBrush(AppTheme.TextSecondary);

            if (data.Count == 0)
            {
                var message = "No usage tracked this week yet.";
                var messageSize = g.MeasureString(message, AppTheme.Base);
                g.DrawString(message, AppTheme.Base, axisBrush,
                    (Width - messageSize.Width) / 2, (Height - messageSize.Height) / 2);
                return;
            }

            var chartLeft = ChartPadding + LeftAxisWidth;
            var chartTop = ChartPadding;
            var chartWidth = Width - chartLeft - ChartPadding;
            var chartHeight = Height - chartTop - ChartPadding - DayLabelAreaHeight;
            if (chartWidth <= 0 || chartHeight <= 0)
            {
                return;
            }

            // Y-axis tops out at the next whole hour above the busiest day, with a
            // floor of 1h so a quiet week doesn't divide by zero.
            var maxHours = Math.Max(1, (int)Math.Ceiling(data.Max(d => d.Total.TotalHours)));

            using (var gridPen = new Pen(AppTheme.Border, 1))
            {
                for (var hour = 0; hour <= maxHours; hour++)
                {
                    var y = chartTop + chartHeight - (int)(chartHeight * (hour / (double)maxHours));
                    g.DrawLine(gridPen, chartLeft, y, chartLeft + chartWidth, y);

                    var label = hour == 0 ? "0" : $"{hour}h";
                    var labelSize = g.MeasureString(label, axisFont);
                    g.DrawString(label, axisFont, axisBrush, chartLeft - labelSize.Width - 6, y - labelSize.Height / 2);
                }
            }

            var dayCount = data.Count;
            var totalGapWidth = BarGap * (dayCount - 1);
            var barWidth = Math.Max(6, (chartWidth - totalGapWidth) / dayCount);

            using var barBrush = new SolidBrush(AppTheme.Accent);

            var x = chartLeft;
            foreach (var (dayLabel, total) in data)
            {
                var fraction = total.TotalHours / maxHours;
                var barHeight = (int)(chartHeight * fraction);
                var barTop = chartTop + chartHeight - barHeight;

                if (barHeight > 0)
                {
                    g.FillRectangle(barBrush, x, barTop, barWidth, barHeight);
                }

                var dayLabelSize = g.MeasureString(dayLabel, axisFont);
                g.DrawString(dayLabel, axisFont, axisBrush,
                    x + (barWidth - dayLabelSize.Width) / 2, chartTop + chartHeight + 4);

                x += barWidth + BarGap;
            }
        }
    }
}