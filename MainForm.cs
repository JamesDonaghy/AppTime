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
        private readonly List<AppSession> sessions;
        private readonly List<SidebarItem> sidebarItems = new();

        private TextBox txtSearch = null!;
        private FlowLayoutPanel libraryFlow = null!;
        private Label libraryHeading = null!;
        private SidebarItem? selectedSidebarItem;
        private Panel libraryPanel = null!;
        private Panel overviewPanel = null!;
        private TableLayoutPanel overviewRecentTable = null!;
        private Panel usagePanel = null!;
        private Panel historyPanel = null!;

        // Polls every few seconds for whether each app's process is currently running,
        // and accumulates usage time while it is. A WinForms Timer ticks on the UI
        // thread, so no cross-thread Invoke is needed to update the cards from it.
        private readonly System.Windows.Forms.Timer runningCheckTimer;
        private DateTime lastRunningCheckTime;

        // Tracks apps currently mid-session (running as of the last tick) and when
        // that session started, so a session record can be created once the app
        // stops running. Keyed by AppEntry.Id.
        private readonly Dictionary<Guid, DateTime> activeSessionStarts = new();

        // Usage time is saved every few ticks rather than on every single one, so a
        // library with something open for hours doesn't rewrite the file every 3
        // seconds. Anything not yet flushed is still saved when the app closes.
        private const int SaveIntervalTicks = 10;
        private int ticksSinceLastSave;

        public MainForm()
        {
            allApps = LibraryStorage.LoadLibrary();
            sessions = SessionStorage.LoadSessions();

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

            lastRunningCheckTime = DateTime.Now;
            runningCheckTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            runningCheckTimer.Tick += (_, _) =>
            {
                TrackRunningApplications();
                RefreshRunningStates();
            };
            runningCheckTimer.Start();

            FormClosed += (_, _) =>
            {
                runningCheckTimer.Dispose();

                // Flush anything accumulated since the last periodic save, so closing
                // AppTime right after using something doesn't lose that last stretch
                // of usage time.
                LibraryStorage.SaveLibrary(allApps);

                // Close out any sessions still in progress - we can't observe them
                // past this point anyway, so ending them now (rather than losing them)
                // is the honest-enough approach.
                if (activeSessionStarts.Count > 0)
                {
                    var closedAt = DateTime.Now;
                    foreach (var (appId, startTime) in activeSessionStarts)
                    {
                        sessions.Add(new AppSession { AppId = appId, StartTime = startTime, EndTime = closedAt });
                    }
                    activeSessionStarts.Clear();
                    SessionStorage.SaveSessions(sessions);
                }
            };
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
            row.Controls.Add(BuildMainContent(), 1, 0);

            return row;
        }

        // Overview and the app library occupy the same area and are swapped by
        // toggling Visible, rather than each sidebar click rebuilding the layout.
        private Panel BuildMainContent()
        {
            var container = new Panel { Dock = DockStyle.Fill };

            libraryPanel = BuildLibraryArea();
            libraryPanel.Visible = false;

            overviewPanel = BuildOverviewPanel();
            overviewPanel.Visible = true;

            usagePanel = BuildPlaceholderPanel("Usage");
            usagePanel.Visible = false;

            historyPanel = BuildPlaceholderPanel("History");
            historyPanel.Visible = false;

            container.Controls.Add(libraryPanel);
            container.Controls.Add(overviewPanel);
            container.Controls.Add(usagePanel);
            container.Controls.Add(historyPanel);

            return container;
        }

        // Usage and History don't have any real content yet - just enough of a view
        // to be a real navigation destination while the actual features are built.
        private Panel BuildPlaceholderPanel(string title)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.Background,
                Padding = new Padding(28, 16, 28, 20)
            };

            var heading = new Label
            {
                Text = title,
                AutoSize = true,
                Location = new Point(0, 0),
                Font = AppTheme.SectionHeading,
                ForeColor = AppTheme.TextSecondary
            };

            var subtext = new Label
            {
                Text = "Work in progress.",
                AutoSize = true,
                Location = new Point(0, 32),
                Font = AppTheme.Base,
                ForeColor = AppTheme.TextSecondary
            };

            panel.Controls.Add(heading);
            panel.Controls.Add(subtext);

            return panel;
        }

        // Number of columns in the Recent/Most Used grid - also how many apps are shown.
        private const int RecentAppsColumnCount = 5;

        private Panel BuildOverviewPanel()
        {
            // Rough first pass at the real Overview layout - a row of stat cards, a
            // Recent/Most Used row reusing the existing AppCard, and an empty
            // placeholder for where the Usage Today chart will go. "Today / This
            // Week / Sessions" are just dashes for now since there's no per-day or
            // per-session tracking yet - that's a bigger feature on its own.
            //
            // A single-column TableLayoutPanel stacks the sections top to bottom -
            // each row is Dock=Top full width, so the stat cards, recent apps grid
            // and usage placeholder all line up to the same width automatically.
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.Background,
                Padding = new Padding(28, 16, 28, 20)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                AutoScroll = true,
                BackColor = AppTheme.Background
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 114f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 154f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 160f));

            var heading = new Label
            {
                Text = "Overview",
                AutoSize = true,
                Font = AppTheme.SectionHeading,
                ForeColor = AppTheme.TextSecondary
            };
            layout.Controls.Add(heading, 0, 0);

            var statsTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 90,
                ColumnCount = 3,
                RowCount = 1
            };
            statsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            statsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            statsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            statsTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            AddStatCard(statsTable, "Today", "—", 0, 3);
            AddStatCard(statsTable, "This Week", "—", 1, 3);
            AddStatCard(statsTable, "Sessions", "—", 2, 3);
            layout.Controls.Add(statsTable, 0, 1);

            var recentHeading = new Label
            {
                Text = "Recent / Most Used",
                AutoSize = true,
                Font = AppTheme.SectionHeading,
                ForeColor = AppTheme.TextSecondary
            };
            layout.Controls.Add(recentHeading, 0, 2);

            overviewRecentTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 130,
                ColumnCount = RecentAppsColumnCount,
                RowCount = 1
            };
            for (var i = 0; i < RecentAppsColumnCount; i++)
            {
                overviewRecentTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / RecentAppsColumnCount));
            }
            overviewRecentTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.Controls.Add(overviewRecentTable, 0, 3);

            var usageHeading = new Label
            {
                Text = "Usage Today",
                AutoSize = true,
                Font = AppTheme.SectionHeading,
                ForeColor = AppTheme.TextSecondary
            };
            layout.Controls.Add(usageHeading, 0, 4);

            var usagePlaceholder = new RoundedPanel
            {
                Dock = DockStyle.Top,
                Height = 140
            };
            usagePlaceholder.Controls.Add(new Label
            {
                Text = "Your usage for today will appear here once you've tracked some application time.",
                AutoSize = true,
                MaximumSize = new Size(420, 0),
                Location = new Point(16, 16),
                Font = AppTheme.Base,
                ForeColor = AppTheme.TextSecondary
            });
            layout.Controls.Add(usagePlaceholder, 0, 5);

            panel.Controls.Add(layout);

            PopulateOverviewRecentApps();

            return panel;
        }

        // Adds a stat card to the given cell, with a small gap to its neighbours
        // (none on the outer edges) so the row lines up flush with the section above.
        private void AddStatCard(TableLayoutPanel table, string label, string value, int column, int columnCount)
        {
            var card = BuildStatCard(label, value);
            card.Dock = DockStyle.Fill;
            card.Margin = GridCellMargin(column, columnCount, gap: 16);
            table.Controls.Add(card, column, 0);
        }

        private static Padding GridCellMargin(int column, int columnCount, int gap)
        {
            var left = column == 0 ? 0 : gap / 2;
            var right = column == columnCount - 1 ? 0 : gap / 2;
            return new Padding(left, 0, right, 0);
        }

        private Panel BuildStatCard(string label, string value)
        {
            var card = new RoundedPanel();

            card.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Location = new Point(16, 14),
                Font = AppTheme.SmallText,
                ForeColor = AppTheme.TextSecondary
            });

            card.Controls.Add(new Label
            {
                Text = value,
                AutoSize = true,
                Location = new Point(16, 38),
                Font = AppTheme.CardTitle,
                ForeColor = AppTheme.TextPrimary
            });

            return card;
        }

        // Rebuilds the Recent/Most Used row from scratch, same pattern as ApplyFilter
        // rebuilding libraryFlow. Simpler than diffing the existing cards.
        private void PopulateOverviewRecentApps()
        {
            overviewRecentTable.Controls.Clear();

            var topApps = allApps.OrderByDescending(a => a.TotalUsageTime).Take(RecentAppsColumnCount).ToList();

            if (topApps.Count == 0)
            {
                overviewRecentTable.Controls.Add(new Label
                {
                    Text = "No usage tracked yet.",
                    AutoSize = true,
                    Font = AppTheme.Base,
                    ForeColor = AppTheme.TextSecondary
                }, 0, 0);
            }
            else
            {
                for (var i = 0; i < topApps.Count; i++)
                {
                    var app = topApps[i];
                    var card = new AppCard(app)
                    {
                        Dock = DockStyle.Fill,
                        Margin = GridCellMargin(i, RecentAppsColumnCount, gap: 16)
                    };
                    card.LaunchRequested += (_, _) => LaunchApplication(app);
                    card.EditRequested += (_, _) => EditApplication(app);
                    card.RemoveRequested += (_, _) => RemoveApplication(app);
                    overviewRecentTable.Controls.Add(card, i, 0);
                }
            }

            RefreshRunningStates();
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

            AddSidebarItem(flow, "Overview", category: null, viewKey: "Overview");
            AddSidebarItem(flow, "All Apps", category: null);
            AddSidebarItem(flow, "Creative", category: "Creative");
            AddSidebarItem(flow, "Development", category: "Development");
            AddSidebarItem(flow, "Games", category: "Games");
            AddSidebarItem(flow, "Utilities", category: "Utilities");

            var insightsHeader = new Label
            {
                Text = "INSIGHTS",
                AutoSize = false,
                Width = 180,
                Height = 28,
                Font = new Font(AppTheme.SmallText, FontStyle.Bold),
                ForeColor = AppTheme.TextSecondary,
                Padding = new Padding(4, 12, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };
            flow.Controls.Add(insightsHeader);

            AddSidebarItem(flow, "Usage", category: null, viewKey: "Usage");
            AddSidebarItem(flow, "History", category: null, viewKey: "History");

            panel.Controls.Add(flow);
            panel.Controls.Add(divider);

            return panel;
        }

        private void AddSidebarItem(FlowLayoutPanel flow, string text, string? category, string? viewKey = null)
        {
            var item = new SidebarItem(text, category, viewKey) { Width = 180 };
            item.Click += SidebarItem_Click;

            sidebarItems.Add(item);
            flow.Controls.Add(item);

            // Overview is the default view when AppTime starts.
            if (viewKey == "Overview")
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

            if (clicked.ViewKey is not null)
            {
                libraryPanel.Visible = false;
                overviewPanel.Visible = clicked.ViewKey == "Overview";
                usagePanel.Visible = clicked.ViewKey == "Usage";
                historyPanel.Visible = clicked.ViewKey == "History";

                if (clicked.ViewKey == "Overview")
                {
                    PopulateOverviewRecentApps();
                }

                return;
            }

            overviewPanel.Visible = false;
            usagePanel.Visible = false;
            historyPanel.Visible = false;
            libraryPanel.Visible = true;
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

            libraryHeading = new Label
            {
                Text = "YOUR APPLICATIONS",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
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
            headerRow.Controls.Add(libraryHeading);
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

            // Small, simple stat line - just a live count and total tracked time for
            // whatever's currently shown. Not a full history/statistics view yet.
            libraryHeading.Text = apps.Count == 0
                ? "YOUR APPLICATIONS"
                : $"YOUR APPLICATIONS  ·  {apps.Count} {(apps.Count == 1 ? "app" : "apps")}  ·  {FormatTrackedTotal(apps)}";

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
                    card.EditRequested += (_, _) => EditApplication(app);
                    card.RemoveRequested += (_, _) => RemoveApplication(app);
                    libraryFlow.Controls.Add(card);
                }
            }

            libraryFlow.ResumeLayout();

            // Freshly created cards default to "not running" until the next timer
            // tick - refresh immediately so switching filters or adding/editing an app
            // doesn't show a stale state for a few seconds.
            RefreshRunningStates();
        }

        private static string FormatTrackedTotal(List<AppEntry> apps)
        {
            var total = TimeSpan.Zero;
            foreach (var app in apps)
            {
                total += app.TotalUsageTime;
            }

            if (total.TotalMinutes < 1)
            {
                return "nothing tracked yet";
            }

            var hours = (int)total.TotalHours;
            var minutes = total.Minutes;
            return hours > 0 ? $"{hours}h {minutes}m tracked" : $"{minutes}m tracked";
        }

        private void TrackRunningApplications()
        {
            var now = DateTime.Now;
            var elapsed = now - lastRunningCheckTime;
            lastRunningCheckTime = now;

            // If way more time passed than the timer interval - the PC was asleep, or
            // this process was suspended - don't credit the whole gap as usage time.
            // Counting one normal interval's worth is a simple, honest-enough fallback
            // rather than a large, obviously-wrong jump in the total.
            if (elapsed > TimeSpan.FromSeconds(30))
            {
                elapsed = TimeSpan.FromSeconds(3);
            }

            var anyRunning = false;
            var sessionsChanged = false;

            foreach (var app in allApps)
            {
                var running = IsProcessRunning(app);

                if (running)
                {
                    if (!activeSessionStarts.ContainsKey(app.Id))
                    {
                        // First tick we've seen this app running since it was last
                        // stopped - this is the start of a new session.
                        activeSessionStarts[app.Id] = now;
                    }

                    app.TotalUsageTime += elapsed;
                    app.LastUsed = now;
                    anyRunning = true;
                }
                else if (activeSessionStarts.TryGetValue(app.Id, out var startTime))
                {
                    // Was running as of the last tick and isn't anymore - the session
                    // just ended.
                    sessions.Add(new AppSession { AppId = app.Id, StartTime = startTime, EndTime = now });
                    activeSessionStarts.Remove(app.Id);
                    sessionsChanged = true;
                }
            }

            // Only worth saving if something was actually running, and only every few
            // ticks - see SaveIntervalTicks.
            if (anyRunning && ++ticksSinceLastSave >= SaveIntervalTicks)
            {
                LibraryStorage.SaveLibrary(allApps);
                ticksSinceLastSave = 0;
            }

            // Session records are saved as soon as one ends, rather than batched like
            // the usage total above - session-end events are already infrequent
            // (unlike per-tick accumulation), so there's no need to delay saving them.
            if (sessionsChanged)
            {
                SessionStorage.SaveSessions(sessions);
            }
        }

        private void RefreshRunningStates()
        {
            RefreshRunningStates(libraryFlow.Controls);
            RefreshRunningStates(overviewRecentTable.Controls);
        }

        private void RefreshRunningStates(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                if (control is AppCard card)
                {
                    card.IsRunning = IsProcessRunning(card.App);
                }
            }
        }

        private static bool IsProcessRunning(AppEntry app)
        {
            if (string.IsNullOrWhiteSpace(app.ExecutablePath))
            {
                return false;
            }

            // Matches by process name rather than the exact path - simpler, and good
            // enough for this stage. It means two different apps with the same exe
            // name would be indistinguishable, but that's an edge case worth revisiting
            // only if it actually comes up.
            var processName = Path.GetFileNameWithoutExtension(app.ExecutablePath);
            if (string.IsNullOrEmpty(processName))
            {
                return false;
            }

            var processes = Process.GetProcessesByName(processName);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                // Process objects hold onto native handles until disposed - since we
                // only needed the count, release them immediately rather than waiting
                // on the finalizer.
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
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

        private void EditApplication(AppEntry app)
        {
            using var editForm = new EditApplicationForm(app);
            if (editForm.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            app.Name = editForm.ApplicationName;
            app.Category = editForm.Category;
            app.ExecutablePath = editForm.ExecutablePath;

            LibraryStorage.SaveLibrary(allApps);
            ApplyFilter();
        }

        private void RemoveApplication(AppEntry app)
        {
            // No undo, so confirm first - and default focus to "No" as a small extra
            // safety margin against an accidental Enter press. Uses the themed
            // ConfirmationDialog (matching the Password Manager app's "Confirm Delete")
            // instead of a native MessageBox.
            DialogResult result;
            using (var confirmDialog = new ConfirmationDialog(
                $"Remove \"{app.Name}\" from your library?\n\nThis won't uninstall the application - just removes it from AppTime.",
                "Remove Application"))
            {
                result = confirmDialog.ShowDialog(this);
            }

            if (result != DialogResult.Yes)
            {
                return;
            }

            allApps.Remove(app);
            activeSessionStarts.Remove(app.Id);
            LibraryStorage.SaveLibrary(allApps);
            ApplyFilter();
        }
    }
}