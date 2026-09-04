using System.Drawing;
using System.Windows.Forms;

namespace AppTime
{
    // A small modal confirmation dialog, styled to match the rest of the app instead of
    // falling back to the native MessageBox.
    //
    // We use this instead of MessageBox.Show(owner, ...) for confirmations like "Remove
    // Application": MessageBox is supposed to center itself over the owner window, but
    // that positioning isn't fully reliable in practice (it can land in the center of the
    // screen instead, especially across multiple monitors or certain DPI setups). A regular
    // Form's StartPosition = CenterParent is a real WinForms layout property rather than a
    // native dialog hint, so as long as we call ShowDialog(owner) it always centers over
    // the owner window.
    //
    // Ported from the Password Manager app's ConfirmationDialog (same shape: title bar with
    // a warning glyph, a message that grows the dialog instead of clipping, and a Yes/No
    // action row with Yes styled as the destructive action).
    public class ConfirmationDialog : Form
    {
        public ConfirmationDialog(string message, string title = "Confirm", string yesText = "Yes", string noText = "No")
        {
            this.Text = title;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent; // Requires ShowDialog(owner) to have an effect
            this.BackColor = AppTheme.Background;
            this.Font = AppTheme.Base;

            const int dialogWidth = 400;
            const int contentPadding = 20;
            int messageWidth = dialogWidth - contentPadding * 2;

            var lblFormTitle = new Label
            {
                Text = "⚠️ " + title,
                Dock = DockStyle.Top,
                Height = 36,
                Font = AppTheme.SectionHeading,
                ForeColor = AppTheme.TextPrimary,
                TextAlign = ContentAlignment.BottomLeft,
                Padding = new Padding(contentPadding, 0, 0, 6)
            };

            // Measure how tall the message needs to be at messageWidth before laying out
            // everything below it, so longer messages grow the dialog instead of getting
            // clipped at a fixed height.
            var measuredSize = TextRenderer.MeasureText(
                message,
                AppTheme.Base,
                new Size(messageWidth, int.MaxValue),
                TextFormatFlags.WordBreak);
            int messageHeight = measuredSize.Height + 8;

            var lblMessage = new Label
            {
                Text = message,
                Dock = DockStyle.Top,
                Height = messageHeight,
                Font = AppTheme.Base,
                ForeColor = AppTheme.TextPrimary,
                Padding = new Padding(contentPadding, 0, contentPadding, 0)
            };

            // "Yes" uses the danger button style rather than the usual accent primary -
            // this dialog is only ever used for destructive confirmations, so it shouldn't
            // look like the same "normal" action as Save/Add.
            var btnYes = new Button
            {
                Text = yesText,
                Width = 90,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = AppTheme.Danger,
                ForeColor = Color.White,
                Font = AppTheme.Base,
                Cursor = Cursors.Hand
            };
            btnYes.FlatAppearance.BorderSize = 0;
            btnYes.FlatAppearance.MouseOverBackColor = AppTheme.DangerHover;
            btnYes.FlatAppearance.MouseDownBackColor = AppTheme.DangerHover;
            btnYes.Click += (s, e) => { this.DialogResult = DialogResult.Yes; this.Close(); };

            var btnNo = new Button
            {
                Text = noText,
                Width = 90,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = AppTheme.PanelBackground,
                ForeColor = AppTheme.TextPrimary,
                Font = AppTheme.Base,
                Cursor = Cursors.Hand
            };
            btnNo.FlatAppearance.BorderSize = 1;
            btnNo.FlatAppearance.BorderColor = AppTheme.Border;
            btnNo.FlatAppearance.MouseOverBackColor = AppTheme.AccentSubtle;
            btnNo.FlatAppearance.MouseDownBackColor = AppTheme.AccentSubtle;
            btnNo.Click += (s, e) => { this.DialogResult = DialogResult.No; this.Close(); };

            const int actionPanelHeight = 62;
            var actionPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(contentPadding, 8, contentPadding, 16), BackColor = AppTheme.Background };
            btnNo.Margin = new Padding(0, 3, 0, 3);
            btnYes.Margin = new Padding(10, 3, 0, 3);
            actionPanel.Controls.Add(btnNo);
            actionPanel.Controls.Add(btnYes);

            this.Controls.Add(lblMessage);
            this.Controls.Add(actionPanel);
            this.Controls.Add(lblFormTitle);

            this.AcceptButton = btnNo; // Enter defaults to the safe choice
            this.CancelButton = btnNo; // Esc always cancels the destructive action

            this.ClientSize = new Size(dialogWidth, lblFormTitle.Height + messageHeight + actionPanelHeight);
        }
    }
}