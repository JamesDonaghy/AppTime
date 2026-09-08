using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AppTime
{
    /// A basic vertical bar chart - one bar per app, height relative to the app with
    /// the most usage. No axes, gridlines, or interactivity yet; just enough to show
    /// today's usage at a glance instead of the old "coming soon" placeholder.
    public class UsageTodayChart : Panel
    {
        private const int Padding = 12;
        private const int LabelAreaHeight = 34;
        private const int ValueLabelAreaHeight = 18;
        private const int BarGap = 12;

        private List<(string Name, TimeSpan Duration)> data = new();

        public UsageTodayChart()
        {
            BackColor = AppTheme.CardBackground;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void SetData(List<(string Name, TimeSpan Duration)> usage)
        {
            data = usage;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using var nameFont = new Font(AppTheme.SmallText, FontStyle.Regular);
            using var durationFont = new Font(AppTheme.SmallText, FontStyle.Bold);
            using var textBrush = new SolidBrush(AppTheme.TextSecondary);

            if (data.Count == 0)
            {
                var message = "No usage tracked today yet.";
                var messageSize = g.MeasureString(message, AppTheme.Base);
                g.DrawString(
                    message,
                    AppTheme.Base,
                    textBrush,
                    (Width - messageSize.Width) / 2,
                    (Height - messageSize.Height) / 2);
                return;
            }

            // Reserve room above the bars for the tallest bar's duration label -
            // without this, a bar at 100% height leaves no space for the text above
            // it and it gets clipped against the top edge.
            var chartTop = Padding + ValueLabelAreaHeight;
            var chartAreaHeight = Height - chartTop - Padding - LabelAreaHeight;
            if (chartAreaHeight <= 0)
            {
                return;
            }

            var maxSeconds = data.Max(d => d.Duration.TotalSeconds);
            var barCount = data.Count;
            var totalGapWidth = BarGap * (barCount - 1);
            var barWidth = Math.Max(8, (Width - Padding * 2 - totalGapWidth) / barCount);

            using var barBrush = new SolidBrush(AppTheme.Accent);

            var x = Padding;
            foreach (var (name, duration) in data)
            {
                var fraction = maxSeconds > 0 ? duration.TotalSeconds / maxSeconds : 0;
                var barHeight = Math.Max(4, (int)(chartAreaHeight * fraction));
                var barTop = chartTop + (chartAreaHeight - barHeight);
                var barRect = new Rectangle(x, barTop, barWidth, barHeight);
                g.FillRectangle(barBrush, barRect);

                var durationText = FormatDuration(duration);
                var durationSize = g.MeasureString(durationText, durationFont);
                g.DrawString(
                    durationText,
                    durationFont,
                    textBrush,
                    x + (barWidth - durationSize.Width) / 2,
                    barTop - durationSize.Height - 2);

                var displayName = TruncateToFit(g, name, nameFont, barWidth);
                var nameSize = g.MeasureString(displayName, nameFont);
                g.DrawString(
                    displayName,
                    nameFont,
                    textBrush,
                    x + (barWidth - nameSize.Width) / 2,
                    chartTop + chartAreaHeight + 6);

                x += barWidth + BarGap;
            }
        }

        private static string TruncateToFit(Graphics g, string text, Font font, int maxWidth)
        {
            if (g.MeasureString(text, font).Width <= maxWidth)
            {
                return text;
            }

            const string ellipsis = "…";
            var truncated = text;
            while (truncated.Length > 1 && g.MeasureString(truncated + ellipsis, font).Width > maxWidth)
            {
                truncated = truncated[..^1];
            }

            return truncated + ellipsis;
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalMinutes < 1)
            {
                return "<1m";
            }

            var hours = (int)duration.TotalHours;
            var minutes = duration.Minutes;
            return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
        }
    }
}