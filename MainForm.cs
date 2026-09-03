using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AppTime
{
    public class MainForm : Form
    {
        private readonly List<AppEntry> allApps;
        private readonly List<SidebarItem> sidebarItems = new();

        private TextBox txtSearch = null!;
        private FlowLayoutPanel libraryFlow = null!;
        private SidebarItem? selectedSidebarItem;

        public MainForm()
        {
            allApps = LibraryStorage.LoadLibrary();

            Text = "AppTime";
            BackColor = AppTheme.Background;
            Font = AppTheme.Base;
            MinimumSize = new Size(1000, 650);
            Size = new Size(1180, 720);
            StartPosition = FormStartPosition.CenterScreen;

            // Owner-drawn child controls (AppCard, SidebarItem) look considerably worse
            // without this - without it, resizing/scrolling causes visible flicker.
            DoubleBuffered = true;

            SuspendLayout();
            InitializeComponent();
            ResumeLayout(false);

            ApplyFilter();
        }

        private void InitializeComponent()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = AppTheme.Background
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            root.Controls.Add(BuildTopBar(), 0, 0);
            root.Controls.Add(BuildContentRow(), 0, 1);

            Controls.Add(root);
        }

        private Panel BuildTopBar()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.PanelBackground,
                Padding = new Padding(24, 0, 24, 0)
            };

            var divider = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = AppTheme.Border };

            var title = new Label
            {
                Text = "AppTime",
                Dock = DockStyle.Left,
                Width = 160,
                Font = AppTheme.Heading,
                ForeColor = AppTheme.TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft
            };

            // Wrapper panel gives the single-line search box some fixed top/bottom
            // padding so it sits roughly centred in the 64px bar, without needing a
            // Resize handler just to centre one control.
            var searchWrapper = new Panel
            {
                Dock = DockStyle.Right,
                Width = 260,
                Padding = new Padding(0, 18, 0, 18)
            };

            txtSearch = new TextBox
            {
                Dock = DockStyle.Fill,
                PlaceholderText = "Search your library...",
                Font = AppTheme.Base,
                BackColor = AppTheme.CardBackground,
                ForeColor = AppTheme.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };
            txtSearch.TextChanged += (_, _) => ApplyFilter();
            searchWrapper.Controls.Add(txtSearch);

            panel.Controls.Add(searchWrapper);
            panel.Controls.Add(title);
            panel.Controls.Add(divider);

            return panel;
        }

        private TableLayoutPanel BuildContentRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = AppTheme.Background
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            row.Controls.Add(BuildSidebar(), 0, 0);
            row.Controls.Add(BuildLibraryArea(), 1, 0);

            return row;
        }

        private Panel BuildSidebar()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.PanelBackground,
                Padding = new Padding(12, 20, 12, 12)
            };

            var divider = new Panel { Dock = DockStyle.Right, Width = 1, BackColor = AppTheme.Border };

            // TopDown FlowLayoutPanel instead of stacking individually Dock=Top items -
            // items are laid out in the exact order they're added
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = AppTheme.PanelBackground
            };

            var libraryHeader = new Label
            {
                Text = "LIBRARY",
                AutoSize = false,
                Width = 180,
                Height = 28,
                Font = new Font(AppTheme.SmallText, FontStyle.Bold),
                ForeColor = AppTheme.TextSecondary,
                Padding = new Padding(4, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };
            flow.Controls.Add(libraryHeader);

            AddSidebarItem(flow, "All Apps", category: null);
            AddSidebarItem(flow, "Creative", category: "Creative");
            AddSidebarItem(flow, "Development", category: "Development");
            AddSidebarItem(flow, "Games", category: "Games");
            AddSidebarItem(flow, "Utilities", category: "Utilities");

            panel.Controls.Add(flow);
            panel.Controls.Add(divider);

            return panel;
        }

        private void AddSidebarItem(FlowLayoutPanel flow, string text, string? category)
        {
            var item = new SidebarItem(text, category) { Width = 180 };
            item.Click += SidebarItem_Click;

            sidebarItems.Add(item);
            flow.Controls.Add(item);

            // "All Apps" (no category) is the sensible default view.
            if (category is null)
            {
                item.IsSelected = true;
                selectedSidebarItem = item;
            }
        }

        private void SidebarItem_Click(object? sender, EventArgs e)
        {
            if (sender is not SidebarItem clicked)
            {
                return;
            }

            if (selectedSidebarItem is not null)
            {
                selectedSidebarItem.IsSelected = false;
            }

            clicked.IsSelected = true;
            selectedSidebarItem = clicked;

            ApplyFilter();
        }

        private Panel BuildLibraryArea()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.Background,
                Padding = new Padding(28, 16, 28, 20)
            };

            var headerRow = new Panel { Dock = DockStyle.Top, Height = 36 };

            var heading = new Label
            {
                Text = "YOUR APPLICATIONS",
                Dock = DockStyle.Fill,
                Font = AppTheme.SectionHeading,
                ForeColor = AppTheme.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var btnAddApplication = new Button
            {
                Text = "+ Add Application",
                Dock = DockStyle.Right,
                Width = 150,
                FlatStyle = FlatStyle.Flat,
                BackColor = AppTheme.Accent,
                ForeColor = Color.White,
                Font = AppTheme.Base
            };
            btnAddApplication.FlatAppearance.BorderSize = 0;
            btnAddApplication.Click += BtnAddApplication_Click;

            // Fill-docked heading added before the Right-docked button, so the button
            // claims its fixed slice on the right and the heading fills the rest -
            // same Fill-then-edge ordering used throughout this layout.
            headerRow.Controls.Add(heading);
            headerRow.Controls.Add(btnAddApplication);

            libraryFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = true,
                BackColor = AppTheme.Background,
                Padding = new Padding(0, 8, 0, 0)
            };

            // Fill-docked control added before the Top-docked header row, so the header
            // takes its slice from the top and the flow panel fills what's left below it
            panel.Controls.Add(libraryFlow);
            panel.Controls.Add(headerRow);

            return panel;
        }

        private void BtnAddApplication_Click(object? sender, EventArgs e)
        {
            using var openFileDialog = new OpenFileDialog
            {
                Title = "Choose an application",
                Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (openFileDialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            using var addForm = new AddApplicationForm(openFileDialog.FileName);
            if (addForm.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            allApps.Add(new AppEntry
            {
                Name = addForm.ApplicationName,
                ExecutablePath = openFileDialog.FileName,
                Category = addForm.Category
            });

            LibraryStorage.SaveLibrary(allApps);
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            libraryFlow.SuspendLayout();
            libraryFlow.Controls.Clear();

            IEnumerable<AppEntry> filtered = allApps;

            if (selectedSidebarItem?.FilterCategory is { } category)
            {
                filtered = filtered.Where(a => a.Category == category);
            }

            var searchTerm = txtSearch.Text.Trim();
            if (!string.IsNullOrEmpty(searchTerm))
            {
                filtered = filtered.Where(a => a.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
            }

            var apps = filtered.OrderBy(a => a.Name).ToList();

            if (apps.Count == 0)
            {
                var message = allApps.Count == 0
                    ? "Your library is empty. Click \"+ Add Application\" to add your first app."
                    : "No applications match your current filter.";

                libraryFlow.Controls.Add(new Label
                {
                    Text = message,
                    AutoSize = true,
                    Font = AppTheme.Base,
                    ForeColor = AppTheme.TextSecondary,
                    Margin = new Padding(4, 12, 0, 0)
                });
            }
            else
            {
                foreach (var app in apps)
                {
                    var card = new AppCard(app);
                    card.LaunchRequested += (_, _) => LaunchApplication(app);
                    libraryFlow.Controls.Add(card);
                }
            }

            libraryFlow.ResumeLayout();
        }

        private void LaunchApplication(AppEntry app)
        {
            if (string.IsNullOrWhiteSpace(app.ExecutablePath) || !File.Exists(app.ExecutablePath))
            {
                MessageBox.Show(this, $"Couldn't find \"{app.ExecutablePath}\".\n\nCheck the application's file path.",
                    "Application not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // UseShellExecute rather than launching the exe directly - lets Windows
                // handle things like elevation prompts the same way double-clicking the
                // exe in Explorer would.
                Process.Start(new ProcessStartInfo(app.ExecutablePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Couldn't launch \"{app.Name}\".\n\n{ex.Message}",
                    "Launch failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}