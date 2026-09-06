using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AppTime
{
    /// A plain panel with the same rounded-corner, bordered look as AppCard, without
    /// any of AppCard's app-specific behaviour (launch/edit/remove, running state).
    /// Used for the Overview stat cards and placeholder sections so they match the
    /// app's existing card styling instead of a plain system-drawn rectangle.
    public class RoundedPanel : Panel
    {
        private const int CornerRadius = 10;

        public RoundedPanel()
        {
            BackColor = AppTheme.CardBackground;

            // Owner-drawn - same reasoning as AppCard: avoids flicker on resize/redraw.
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedRect(bounds, CornerRadius);

            using (var backBrush = new SolidBrush(BackColor))
            {
                g.FillPath(backBrush, path);
            }

            using (var borderPen = new Pen(AppTheme.Border, 1))
            {
                g.DrawPath(borderPen, path);
            }
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