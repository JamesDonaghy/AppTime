using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AppTime
{
    /// Small modal dialog for turning a chosen .exe into a library entry. The caller
    /// picks the executable first (via OpenFileDialog) and passes its path in - this
    /// form is only responsible for collecting the display name and category.
    ///
    /// Follows the same pattern as the Password Manager's AddEntryForm: the result is
    /// read back through public properties after ShowDialog() returns DialogResult.OK.
    public class AddApplicationForm : Form
    {
        private readonly string executablePath;

        private TextBox txtName = null!;
        private ComboBox cmbCategory = null!;

        public string ApplicationName { get; private set; } = string.Empty;
        public string Category { get; private set; } = string.Empty;

        public AddApplicationForm(string executablePath, string? preferredCategory = null)
        {
            this.executablePath = executablePath;

            Text = "Add Application";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(380, 230);
            BackColor = AppTheme.Background;
            Font = AppTheme.Base;

            InitializeComponent();

            if (!string.IsNullOrWhiteSpace(preferredCategory))
            {
                cmbCategory.Text = preferredCategory;
            }
        }

        private void InitializeComponent()
        {
            // Explicit row/column placement rather than a stack of Dock=Top controls -
            // with six rows to get right, this is far less error-prone than relying on
            // Dock add-order.
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(20)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var lblPath = new Label
            {
                Text = executablePath,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = AppTheme.SmallText,
                ForeColor = AppTheme.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var lblNameCaption = new Label
            {
                Text = "Name",
                Dock = DockStyle.Fill,
                Font = new Font(AppTheme.SmallText, FontStyle.Bold),
                ForeColor = AppTheme.TextSecondary,
                TextAlign = ContentAlignment.BottomLeft
            };

            txtName = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = AppTheme.Base,
                Text = Path.GetFileNameWithoutExtension(executablePath)
            };

            var lblCategoryCaption = new Label
            {
                Text = "Category",
                Dock = DockStyle.Fill,
                Font = new Font(AppTheme.SmallText, FontStyle.Bold),
                ForeColor = AppTheme.TextSecondary,
                TextAlign = ContentAlignment.BottomLeft
            };

            // Editable, so an existing category can be picked or a new one typed - the
            // sidebar only shows fixed categories for now, but the model doesn't
            // restrict the value to that list.
            cmbCategory = new ComboBox
            {
                Dock = DockStyle.Fill,
                Font = AppTheme.Base,
                DropDownStyle = ComboBoxStyle.DropDown
            };
            cmbCategory.Items.AddRange(new object[] { "Creative", "Development", "Games", "Utilities" });

            var buttonRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };

            var btnSave = new Button
            {
                Text = "Add",
                Width = 90,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = AppTheme.Accent,
                ForeColor = Color.White,
                Font = AppTheme.Base
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += BtnSave_Click;

            var btnCancel = new Button
            {
                Text = "Cancel",
                Width = 90,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = AppTheme.PanelBackground,
                ForeColor = AppTheme.TextPrimary,
                Font = AppTheme.Base,
                DialogResult = DialogResult.Cancel
            };
            btnCancel.FlatAppearance.BorderSize = 1;
            btnCancel.FlatAppearance.BorderColor = AppTheme.Border;

            // RightToLeft flow: the first control added ends up rightmost, so adding
            // Save before Cancel gives the conventional [Cancel] [Add] reading order.
            buttonRow.Controls.Add(btnSave);
            buttonRow.Controls.Add(btnCancel);

            root.Controls.Add(lblPath, 0, 0);
            root.Controls.Add(lblNameCaption, 0, 1);
            root.Controls.Add(txtName, 0, 2);
            root.Controls.Add(lblCategoryCaption, 0, 3);
            root.Controls.Add(cmbCategory, 0, 4);
            root.Controls.Add(buttonRow, 0, 5);

            Controls.Add(root);

            AcceptButton = btnSave;
            CancelButton = btnCancel;
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            var name = txtName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show(this, "Please enter a name for the application.", "Name required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtName.Focus();
                return;
            }

            var category = cmbCategory.Text.Trim();

            ApplicationName = name;
            Category = string.IsNullOrEmpty(category) ? "Uncategorised" : category;

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}