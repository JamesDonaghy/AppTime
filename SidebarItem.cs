using System.Drawing;
using System.Windows.Forms;

namespace AppTime
{
    /// A single clickable row in the sidebar (e.g. "All Apps", "Creative"). Owner-drawn
    /// for the same reason as AppCard - a selection highlight and hover state are easier
    /// to keep in sync from one Paint method than from a Label plus manual BackColor
    /// juggling on every click.
    public class SidebarItem : Panel
    {
        private bool isHovered;
        private bool isSelected;

        public string DisplayText { get; }

        // The value this item filters the library by. Null means "no filter" (All Apps).
        public string? FilterCategory { get; }

        public bool IsSelected
        {
            get => isSelected;
            set { isSelected = value; Invalidate(); }
        }

        public SidebarItem(string text, string? filterCategory)
        {
            DisplayText = text;
            FilterCategory = filterCategory;

            // Sized explicitly rather than docked - this control lives inside a
            // TopDown FlowLayoutPanel, which positions children itself and doesn't use
            // the Dock property.
            Height = 34;
            Cursor = Cursors.Hand;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            MouseEnter += (_, _) => { isHovered = true; Invalidate(); };
            MouseLeave += (_, _) => { isHovered = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;

            Color backColor;
            Color textColor;

            if (isSelected)
            {
                backColor = AppTheme.AccentSubtle;
                textColor = AppTheme.Accent;
            }
            else if (isHovered)
            {
                backColor = AppTheme.CardBackground;
                textColor = AppTheme.TextPrimary;
            }
            else
            {
                backColor = AppTheme.PanelBackground;
                textColor = AppTheme.TextSecondary;
            }

            using (var backBrush = new SolidBrush(backColor))
            {
                g.FillRectangle(backBrush, ClientRectangle);
            }

            // Thin accent bar down the left edge of the selected item, a common "you are
            // here" pattern in library-style side navs.
            if (isSelected)
            {
                using var accentPen = new Pen(AppTheme.Accent, 3);
                g.DrawLine(accentPen, 0, 2, 0, Height - 3);
            }

            var textRect = new Rectangle(18, 0, Width - 18, Height);
            TextRenderer.DrawText(g, DisplayText, AppTheme.Base, textRect, textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }
    }
}