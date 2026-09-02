using System;
using System.Collections.Generic;
using System.Drawing;
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
            allApps = SampleLibrary.GetSampleApps();

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

            var heading = new Label
            {
                Text = "YOUR APPLICATIONS",
                Dock = DockStyle.Top,
                Height = 32,
                Font = AppTheme.SectionHeading,
                ForeColor = AppTheme.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft
            };

            libraryFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = true,
                BackColor = AppTheme.Background,
                Padding = new Padding(0, 8, 0, 0)
            };

            // Fill-docked control added before the Top-docked heading, so the heading
            // takes its slice from the top and the flow panel fills what's left below
            // it
            panel.Controls.Add(libraryFlow);
            panel.Controls.Add(heading);

            return panel;
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

            foreach (var app in filtered.OrderBy(a => a.Name))
            {
                libraryFlow.Controls.Add(new AppCard(app));
            }

            libraryFlow.ResumeLayout();
        }
    }
}
