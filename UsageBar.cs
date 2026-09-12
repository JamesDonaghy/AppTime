using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AppTime
{
    /// A slim horizontal indicator with rounded ends showing an app's share of
    /// tracked time - used in "Most Used Applications". Owner-drawn for the same
    /// reason as RoundedPanel: a stock ProgressBar can't give rounded corners, and
    /// owner-drawing avoids flicker on resize.
    public class UsageBar : Panel
    {
        private const int BarThickness = 6;

        private double percent;

        public Color FillColor { get; set; } = AppTheme.Accent;

        public UsageBar()
        {
            BackColor = Color.Transparent;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.SupportsTransparentBackColor,
                true);
        }

        public void SetPercent(double value)
        {
            percent = Math.Clamp(value, 0, 100);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var trackTop = (Height - BarThickness) / 2;
            var trackRect = new Rectangle(0, trackTop, Math.Max(BarThickness, Width), BarThickness);

            using (var trackPath = RoundedRect(trackRect, BarThickness / 2))
            using (var trackBrush = new SolidBrush(AppTheme.Border))
            {
                g.FillPath(trackBrush, trackPath);
            }

            var fillWidth = (int)(trackRect.Width * (percent / 100.0));
            if (fillWidth <= 0)
            {
                return;
            }

            // Never let the fill drop below its own thickness, or the rounded caps
            // overlap oddly at very small percentages.
            fillWidth = Math.Min(Math.Max(fillWidth, BarThickness), trackRect.Width);
            var fillRect = new Rectangle(0, trackTop, fillWidth, BarThickness);

            using var fillPath = RoundedRect(fillRect, BarThickness / 2);
            using var fillBrush = new SolidBrush(FillColor);
            g.FillPath(fillBrush, fillPath);
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();

            if (bounds.Width <= diameter)
            {
                path.AddEllipse(bounds);
                path.CloseFigure();
                return path;
            }

            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 90, 180);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 180);
            path.CloseFigure();

            return path;
        }
    }
}