using System;
using System.Drawing;
using System.Windows.Forms;

namespace AppTime
{
    /// Modal dialog for editing an existing library entry - name, category, and
    /// (optionally) which executable it points to. Mirrors AddApplicationForm's shape
    /// and conventions, but pre-fills from an existing AppEntry and allows repointing
    /// the executable via a "Change..." button rather than requiring one to be chosen
    /// up front.
    public class EditApplicationForm : Form
    {
        private string executablePath;

        private Label lblPath = null!;
        private TextBox txtName = null!;
        private ComboBox cmbCategory = null!;

        public string ApplicationName { get; private set; } = string.Empty;
        public string Category { get; private set; } = string.Empty;
        public string ExecutablePath { get; private set; } = string.Empty;

        public EditApplicationForm(AppEntry app)
        {
            executablePath = app.ExecutablePath;

            Text = "Edit Application";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(380, 262);
            BackColor = AppTheme.Background;
            Font = AppTheme.Base;

            InitializeComponent(app);
        }

        private void InitializeComponent(AppEntry app)
        {
            // Explicit row/column placement rather than a stack of Dock=Top controls -
            // same reasoning as AddApplicationForm.
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                Padding = new Padding(20)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); // "Executable" + Change button
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); // path display
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); // "Name" caption
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // name textbox
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); // "Category" caption
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // category combo
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); // buttons
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var pathHeaderRow = new Panel { Dock = DockStyle.Fill };

            var lblPathCaption = new Label
            {
                Text = "Executable",
                Dock = DockStyle.Fill,
                Font = new Font(AppTheme.SmallText, FontStyle.Bold),
                ForeColor = AppTheme.TextSecondary,
                TextAlign = ContentAlignment.BottomLeft
            };

            var btnChangePath = new Button
            {
                Text = "Change...",
                Dock = DockStyle.Right,
                Width = 80,
                Height = 24,
                Margin = new Padding(0, 0, 0, 2),
                FlatStyle = FlatStyle.Flat,
                BackColor = AppTheme.PanelBackground,
                ForeColor = AppTheme.TextPrimary,
                Font = AppTheme.SmallText
            };
            btnChangePath.FlatAppearance.BorderSize = 1;
            btnChangePath.FlatAppearance.BorderColor = AppTheme.Border;
            btnChangePath.Click += BtnChangePath_Click;

            // Fill-docked caption added before the Right-docked button, so the button
            // claims its fixed slice and the caption fills the rest.
            pathHeaderRow.Controls.Add(lblPathCaption);
            pathHeaderRow.Controls.Add(btnChangePath);

            lblPath = new Label
            {
                Text = executablePath,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = AppTheme.SmallText,
                ForeColor = AppTheme.TextSecondary,
                TextAlign = ContentAlignment.TopLeft
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
                Text = app.Name
            };

            var lblCategoryCaption = new Label
            {
                Text = "Category",
                Dock = DockStyle.Fill,
                Font = new Font(AppTheme.SmallText, FontStyle.Bold),
                ForeColor = AppTheme.TextSecondary,
                TextAlign = ContentAlignment.BottomLeft
            };

            cmbCategory = new ComboBox
            {
                Dock = DockStyle.Fill,
                Font = AppTheme.Base,
                DropDownStyle = ComboBoxStyle.DropDown,
                Text = app.Category
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
                Text = "Save",
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

            buttonRow.Controls.Add(btnSave);
            buttonRow.Controls.Add(btnCancel);

            root.Controls.Add(pathHeaderRow, 0, 0);
            root.Controls.Add(lblPath, 0, 1);
            root.Controls.Add(lblNameCaption, 0, 2);
            root.Controls.Add(txtName, 0, 3);
            root.Controls.Add(lblCategoryCaption, 0, 4);
            root.Controls.Add(cmbCategory, 0, 5);
            root.Controls.Add(buttonRow, 0, 6);

            Controls.Add(root);

            AcceptButton = btnSave;
            CancelButton = btnCancel;
        }

        private void BtnChangePath_Click(object? sender, EventArgs e)
        {
            using var openFileDialog = new OpenFileDialog
            {
                Title = "Choose an application",
                Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true,
                FileName = executablePath
            };

            if (openFileDialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            executablePath = openFileDialog.FileName;
            lblPath.Text = executablePath;
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
            ExecutablePath = executablePath;

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}