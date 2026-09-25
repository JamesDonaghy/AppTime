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
        private Panel suggestedAppsSection = null!;
        private FlowLayoutPanel suggestedAppsFlow = null!;
        private Label suggestedAppsSubtitle = null!;
        private Label suggestedAppsActionLink = null!;
        private HashSet<string> ignoredSuggestions = new(StringComparer.OrdinalIgnoreCase);
        private Label libraryHeading = null!;
        private SidebarItem? selectedSidebarItem;
        private Panel libraryPanel = null!;
        private Panel overviewPanel = null!;
        private TableLayoutPanel overviewRecentTable = null!;
        private Label overviewSessionsValueLabel = null!;
        private Label overviewTodayValueLabel = null!;
        private Label overviewThisWeekValueLabel = null!;
        private Panel usagePanel = null!;
        private readonly Dictionary<string, Button> insightsPeriodTabButtons = new();
        private string selectedInsightsPeriod = "This Week";
        private Label insightsTotalTimeValueLabel = null!;
        private Label insightsSessionsCountValueLabel = null!;
        private Label insightsMostUsedAppValueLabel = null!;
        private Label usageChartTitleLabel = null!;
        private WeeklyUsageChart weeklyUsageChart = null!;
        private UsageByCategoryChart usageByCategoryChart = null!;
        private FlowLayoutPanel mostUsedAppsFlow = null!;
        private FlowLayoutPanel insightsTrendsFlow = null!;
        private Panel historyPanel = null!;
        private TableLayoutPanel historyTable = null!;
        private readonly Dictionary<string, Button> periodTabButtons = new();
        private string selectedHistoryPeriod = "Today";
        private Label historySessionsCountValueLabel = null!;
        private Label historyTotalTimeValueLabel = null!;
        private Label historyMostUsedAppValueLabel = null!;
        private Label historySessionsHeadingLabel = null!;
        private Label historySummaryLabel = null!;
        private ComboBox historyAppFilterCombo = null!;
        private ComboBox historyCategoryFilterCombo = null!;
        // null = All Applications / All Categories
        private Guid? selectedHistoryAppFilter;
        private string? selectedHistoryCategoryFilter;
        private bool suppressHistoryFilterEvents;
        // Simple pagination for the Sessions list - only the most recent N rows are
        // materialised as controls; "Load More" reveals the next batch.
        private const int HistoryInitialCount = 7;
        private const int HistoryPageSize = 20;
        // Height of each session row. Deep enough for name + category; padding kept
        // tight so the gap under the text matches the hairline divider.
        private const int SessionRowHeight = 52;
        private List<AppSession> historyFilteredSessions = new();
        private int historyVisibleCount = HistoryInitialCount;
        private UsageTodayChart usageChart = null!;
        private Panel detailsPanel = null!;
        private AppEntry? currentDetailsApp;
        private Label detailsBackLink = null!;
        private PictureBox detailsIconPicture = null!;
        private Label detailsNameLabel = null!;
        private Label detailsCategoryPill = null!;
        private Label detailsUsageValueLabel = null!;
        private Label detailsTodayValueLabel = null!;
        private Label detailsThisWeekValueLabel = null!;
        private Label detailsSessionsCountValueLabel = null!;
        private Panel detailsStatusPanel = null!;
        private Label detailsStatusTitleLabel = null!;
        private Label detailsStatusMetaLabel = null!;
        private Button detailsActionButton = null!;
        private bool detailsActionButtonHovered;
        private Label detailsViewAllSessionsLink = null!;
        private TableLayoutPanel detailsSessionsTable = null!;
        // Profile shows a short recent list only; full history lives on Sessions.
        private const int DetailsRecentSessionCount = 5;

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
            // Session-only: dismissed suggestions return the next time AppTime starts.
            ignoredSuggestions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Text = "AppTime";
            BackColor = AppTheme.Background;
            Font = AppTheme.Base;
            MinimumSize = new Size(1100, 720);
            Size = new Size(1320, 820);
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

            usagePanel = BuildInsightsPanel();
            usagePanel.Visible = false;

            historyPanel = BuildHistoryPanel();
            historyPanel.Visible = false;

            detailsPanel = BuildDetailsPanel();
            detailsPanel.Visible = false;

            container.Controls.Add(libraryPanel);
            container.Controls.Add(overviewPanel);
            container.Controls.Add(usagePanel);
            container.Controls.Add(historyPanel);
            container.Controls.Add(detailsPanel);

            return container;
        }

        // Insights layout: summary cards, weekly chart, category donut, Most Used
        // Applications, and Insights & Trends all use real data.
        private Panel BuildInsightsPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.Background,
                Padding = new Padding(28, 16, 28, 20)
            };

            // Single scrollable column, same approach as BuildOverviewPanel - the page
            // is taller than a typical window, so it scrolls as one block rather than
            // splitting out a separately-scrolling sub-section.
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                AutoScroll = true,
                BackColor = AppTheme.Background
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 280f));

            layout.Controls.Add(BuildInsightsPageTitle(), 0, 0);
            layout.Controls.Add(BuildInsightsPeriodTabsRow(), 0, 1);
            layout.Controls.Add(BuildInsightsSummaryRow(), 0, 2);
            layout.Controls.Add(BuildUsageThisWeekRow(), 0, 3);
            layout.Controls.Add(BuildMostUsedApplicationsRow(), 0, 4);

            panel.Controls.Add(layout);

            PopulateInsights();

            return panel;
        }

        private Panel BuildInsightsPageTitle()
        {
            var titlePanel = new Panel { Dock = DockStyle.Fill };

            titlePanel.Controls.Add(new Label
            {
                Text = "Insights",
                AutoSize = true,
                Location = new Point(0, 0),
                Font = AppTheme.Heading,
                ForeColor = AppTheme.TextPrimary
            });

            titlePanel.Controls.Add(new Label
            {
                Text = "See how you're spending time across your apps and categories.",
                AutoSize = true,
                Location = new Point(0, 28),
                Font = AppTheme.Base,
                ForeColor = AppTheme.TextSecondary
            });

            return titlePanel;
        }

        private FlowLayoutPanel BuildInsightsPeriodTabsRow()
        {
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            foreach (var period in new[] { "This Week", "This Month", "Last 3 Months" })
            {
                var tabButton = new Button
                {
                    Text = period,
                    FlatStyle = FlatStyle.Flat,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Padding = new Padding(14, 6, 14, 6),
                    Margin = new Padding(0, 0, 8, 0),
                    Font = AppTheme.SmallText,
                    Cursor = Cursors.Hand,
                    TabStop = false
                };
                tabButton.FlatAppearance.BorderSize = 0;

                var capturedPeriod = period;
                tabButton.Click += (_, _) =>
                {
                    if (selectedInsightsPeriod == capturedPeriod)
                    {
                        return;
                    }

                    selectedInsightsPeriod = capturedPeriod;
                    PopulateInsights();
                };

                insightsPeriodTabButtons[period] = tabButton;
                row.Controls.Add(tabButton);
            }

            return row;
        }

        private void UpdateInsightsPeriodTabAppearance()
        {
            foreach (var (period, button) in insightsPeriodTabButtons)
            {
                var isSelected = period == selectedInsightsPeriod;
                button.BackColor = isSelected ? AppTheme.Accent : AppTheme.Background;
                button.ForeColor = isSelected ? Color.White : AppTheme.TextSecondary;
            }
        }

        private TableLayoutPanel BuildInsightsSummaryRow()
        {
            var statsRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1
            };
            statsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            statsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            statsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            statsRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            AddStatCard(statsRow, "Total Tracked Time", "—", 0, 3, out insightsTotalTimeValueLabel);
            AddStatCard(statsRow, "Total Sessions", "—", 1, 3, out insightsSessionsCountValueLabel);
            AddStatCard(statsRow, "Most Used App", "—", 2, 3, out insightsMostUsedAppValueLabel);

            return statsRow;
        }

        // "Usage This Week" (real data) alongside "Usage by Category" (donut + legend).
        private TableLayoutPanel BuildUsageThisWeekRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36f));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var chartBox = new RoundedPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
            usageChartTitleLabel = new Label
            {
                Text = "Usage This Week",
                AutoSize = true,
                Location = new Point(16, 14),
                Font = AppTheme.SectionHeading,
                ForeColor = AppTheme.TextPrimary
            };
            chartBox.Controls.Add(usageChartTitleLabel);
            weeklyUsageChart = new WeeklyUsageChart
            {
                Location = new Point(8, 44),
                Size = new Size(10, 10) // resized below once the box has real bounds
            };
            chartBox.Controls.Add(weeklyUsageChart);
            chartBox.Resize += (_, _) => ResizeWeeklyUsageChart(chartBox);
            row.Controls.Add(chartBox, 0, 0);

            row.Controls.Add(BuildUsageByCategoryContainer(new Padding(8, 0, 0, 0)), 1, 0);

            return row;
        }

        private RoundedPanel BuildUsageByCategoryContainer(Padding margin)
        {
            var box = new RoundedPanel { Dock = DockStyle.Fill, Margin = margin };

            box.Controls.Add(new Label
            {
                Text = "Usage by Category",
                AutoSize = true,
                Location = new Point(16, 14),
                Font = AppTheme.SectionHeading,
                ForeColor = AppTheme.TextPrimary
            });

            usageByCategoryChart = new UsageByCategoryChart
            {
                Location = new Point(8, 40),
                Size = new Size(10, 10)
            };
            box.Controls.Add(usageByCategoryChart);
            box.Resize += (_, _) =>
            {
                usageByCategoryChart.SetBounds(
                    8,
                    40,
                    Math.Max(0, box.Width - 16),
                    Math.Max(0, box.Height - 48));
            };

            return box;
        }

        private void ResizeWeeklyUsageChart(Panel chartBox)
        {
            weeklyUsageChart.SetBounds(8, 44, Math.Max(0, chartBox.Width - 16), Math.Max(0, chartBox.Height - 52));
        }

        // Rebuilds every Insights section for the currently selected period tab.
        private void PopulateInsights()
        {
            UpdateInsightsPeriodTabAppearance();

            var periodSessions = GetSessionsForPeriod(selectedInsightsPeriod).ToList();

            // Summary cards
            var totalTicks = periodSessions.Sum(s => Math.Max(0, (s.EndTime - s.StartTime).Ticks));
            insightsTotalTimeValueLabel.Text = FormatDuration(TimeSpan.FromTicks(totalTicks));
            insightsSessionsCountValueLabel.Text = periodSessions.Count.ToString();

            var mostUsed = periodSessions
                .GroupBy(s => s.AppId)
                .Select(g => (
                    Name: allApps.FirstOrDefault(a => a.Id == g.Key)?.Name ?? "Removed app",
                    Ticks: g.Sum(s => Math.Max(0, (s.EndTime - s.StartTime).Ticks))))
                .OrderByDescending(x => x.Ticks)
                .FirstOrDefault();
            insightsMostUsedAppValueLabel.Text = mostUsed.Ticks > 0 ? mostUsed.Name : "—";

            usageChartTitleLabel.Text = selectedInsightsPeriod switch
            {
                "This Month" => "Usage This Month",
                "Last 3 Months" => "Usage Last 3 Months",
                _ => "Usage This Week"
            };

            PopulateUsageChartForPeriod(periodSessions);
            PopulateUsageByCategory(periodSessions);
            PopulateMostUsedApplications(periodSessions);
            PopulateInsightsTrends(periodSessions);
        }

        private void PopulateUsageChartForPeriod(List<AppSession> periodSessions)
        {
            var categoryByAppId = allApps.ToDictionary(
                a => a.Id,
                a => string.IsNullOrWhiteSpace(a.Category) ? "Other" : a.Category);

            List<(string DayLabel, Dictionary<string, TimeSpan> Segments)> buckets;

            if (selectedInsightsPeriod == "Last 3 Months")
            {
                // One bar per calendar month covering the last 3 months (including current).
                var today = DateTime.Today;
                var months = Enumerable.Range(0, 3)
                    .Select(offset => new DateTime(today.Year, today.Month, 1).AddMonths(offset - 2))
                    .ToList();

                buckets = months.Select(monthStart =>
                {
                    var monthEnd = monthStart.AddMonths(1);
                    var segments = AggregateSegments(
                        periodSessions.Where(s => s.StartTime.Date >= monthStart && s.StartTime.Date < monthEnd),
                        categoryByAppId);
                    return (DayLabel: monthStart.ToString("MMM"), Segments: segments);
                }).ToList();
            }
            else if (selectedInsightsPeriod == "This Month")
            {
                // One bar per week of the current month so far (week starts Monday).
                var today = DateTime.Today;
                var monthStart = new DateTime(today.Year, today.Month, 1);
                var firstWeekStart = StartOfWeek(monthStart);
                var bucketsList = new List<(string, Dictionary<string, TimeSpan>)>();
                for (var weekStart = firstWeekStart; weekStart <= today; weekStart = weekStart.AddDays(7))
                {
                    var weekEnd = weekStart.AddDays(7);
                    var segments = AggregateSegments(
                        periodSessions.Where(s => s.StartTime.Date >= weekStart && s.StartTime.Date < weekEnd
                            && s.StartTime.Date >= monthStart && s.StartTime.Date <= today),
                        categoryByAppId);
                    var label = weekStart < monthStart
                        ? monthStart.ToString("d MMM")
                        : weekStart.ToString("d MMM");
                    bucketsList.Add((label, segments));
                }

                buckets = bucketsList;
            }
            else
            {
                // This Week: Mon–Sun daily bars.
                var weekStart = StartOfWeek(DateTime.Today);
                var days = Enumerable.Range(0, 7).Select(offset => weekStart.AddDays(offset)).ToList();
                buckets = days.Select(day =>
                {
                    var segments = AggregateSegments(
                        periodSessions.Where(s => s.StartTime.Date == day),
                        categoryByAppId);
                    return (DayLabel: day.ToString("ddd"), Segments: segments);
                }).ToList();
            }

            weeklyUsageChart.SetData(buckets);
        }

        private static Dictionary<string, TimeSpan> AggregateSegments(
            IEnumerable<AppSession> source,
            Dictionary<Guid, string> categoryByAppId)
        {
            var segments = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
            foreach (var session in source)
            {
                var category = categoryByAppId.TryGetValue(session.AppId, out var cat) ? cat : "Other";
                var duration = session.EndTime - session.StartTime;
                if (duration <= TimeSpan.Zero)
                {
                    continue;
                }

                if (segments.TryGetValue(category, out var existing))
                {
                    segments[category] = existing + duration;
                }
                else
                {
                    segments[category] = duration;
                }
            }

            return segments;
        }

        private void PopulateUsageByCategory(List<AppSession> periodSessions)
        {
            var categoryByAppId = allApps.ToDictionary(
                a => a.Id,
                a => string.IsNullOrWhiteSpace(a.Category) ? "Other" : a.Category);

            var byCategory = periodSessions
                .GroupBy(s => categoryByAppId.TryGetValue(s.AppId, out var cat) ? cat : "Other")
                .Select(g => (
                    Category: g.Key,
                    Duration: TimeSpan.FromTicks(g.Sum(s => Math.Max(0, (s.EndTime - s.StartTime).Ticks)))))
                .Where(c => c.Duration > TimeSpan.Zero)
                .ToList();

            usageByCategoryChart.SetData(byCategory);
        }

        // "Most Used Applications" alongside "Insights & Trends" (both real data).
        private TableLayoutPanel BuildMostUsedApplicationsRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36f));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var listBox = new RoundedPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
            listBox.Controls.Add(new Label
            {
                Text = "Most Used Applications",
                AutoSize = true,
                Location = new Point(16, 14),
                Font = AppTheme.SectionHeading,
                ForeColor = AppTheme.TextPrimary
            });

            mostUsedAppsFlow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = false,
                Location = new Point(16, 46),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            listBox.Controls.Add(mostUsedAppsFlow);
            listBox.Resize += (_, _) =>
            {
                mostUsedAppsFlow.Width = Math.Max(0, listBox.Width - 32);
                mostUsedAppsFlow.Height = Math.Max(0, listBox.Height - 58);
                foreach (Control child in mostUsedAppsFlow.Controls)
                {
                    child.Width = mostUsedAppsFlow.Width;
                }
            };
            row.Controls.Add(listBox, 0, 0);

            row.Controls.Add(BuildInsightsTrendsContainer(new Padding(8, 0, 0, 0)), 1, 0);

            return row;
        }

        private RoundedPanel BuildInsightsTrendsContainer(Padding margin)
        {
            var box = new RoundedPanel { Dock = DockStyle.Fill, Margin = margin };

            box.Controls.Add(new Label
            {
                Text = "Insights & Trends",
                AutoSize = true,
                Location = new Point(16, 14),
                Font = AppTheme.SectionHeading,
                ForeColor = AppTheme.TextPrimary,
                // WinForms treats & as a mnemonic (accelerator); turn that off so the
                // ampersand is shown literally rather than eaten for the next letter.
                UseMnemonic = false
            });

            insightsTrendsFlow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = false,
                Location = new Point(16, 44),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            box.Controls.Add(insightsTrendsFlow);
            box.Resize += (_, _) =>
            {
                insightsTrendsFlow.Width = Math.Max(0, box.Width - 32);
                insightsTrendsFlow.Height = Math.Max(0, box.Height - 56);
                foreach (Control child in insightsTrendsFlow.Controls)
                {
                    child.Width = insightsTrendsFlow.Width;
                }
            };

            return box;
        }

        // Narrative insights derived from the selected period's sessions.
        private void PopulateInsightsTrends(List<AppSession> periodSessions)
        {
            insightsTrendsFlow.Controls.Clear();

            if (insightsTrendsFlow.Parent != null)
            {
                insightsTrendsFlow.Width = Math.Max(180, insightsTrendsFlow.Parent.Width - 32);
            }

            var today = DateTime.Today;
            var periodLabel = selectedInsightsPeriod switch
            {
                "This Month" => "this month",
                "Last 3 Months" => "over the last 3 months",
                _ => "this week"
            };

            var periodTicks = periodSessions.Sum(s => Math.Max(0, (s.EndTime - s.StartTime).Ticks));
            var periodTime = TimeSpan.FromTicks(periodTicks);

            // 1. Activity vs previous comparable period
            var (previousSessions, previousLabel) = GetPreviousPeriodSessions(selectedInsightsPeriod);
            var previousTicks = previousSessions.Sum(s => Math.Max(0, (s.EndTime - s.StartTime).Ticks));
            var previousTime = TimeSpan.FromTicks(previousTicks);

            if (previousTicks > 0)
            {
                var changePercent = (periodTicks - previousTicks) * 100.0 / previousTicks;
                var direction = changePercent >= 0 ? "more" : "less";
                var absPercent = Math.Abs(changePercent);
                insightsTrendsFlow.Controls.Add(BuildTrendItem(
                    $"You're {absPercent:0}% {direction} active {periodLabel}",
                    $"Total tracked time is {(changePercent >= 0 ? "up" : "down")} from {FormatDuration(previousTime)} {previousLabel} to {FormatDuration(periodTime)}."));
            }
            else if (periodTicks > 0)
            {
                insightsTrendsFlow.Controls.Add(BuildTrendItem(
                    $"You're getting started {periodLabel}",
                    $"Total tracked time so far is {FormatDuration(periodTime)}."));
            }
            else
            {
                insightsTrendsFlow.Controls.Add(BuildTrendItem(
                    $"No activity {periodLabel} yet",
                    "Start a session to see how this period compares to the last."));
            }

            // 2. Top category within the selected period
            var categoryByAppId = allApps.ToDictionary(
                a => a.Id,
                a => string.IsNullOrWhiteSpace(a.Category) ? "Other" : a.Category);

            var categoryTotals = periodSessions
                .GroupBy(s => categoryByAppId.TryGetValue(s.AppId, out var cat) ? cat : "Other")
                .Select(g => new
                {
                    Category = g.Key,
                    Ticks = g.Sum(s => Math.Max(0, (s.EndTime - s.StartTime).Ticks))
                })
                .Where(c => c.Ticks > 0)
                .OrderByDescending(c => c.Ticks)
                .ToList();

            if (categoryTotals.Count > 0 && periodTicks > 0)
            {
                var top = categoryTotals[0];
                var topPercent = top.Ticks * 100.0 / periodTicks;
                var topDuration = TimeSpan.FromTicks(top.Ticks);
                insightsTrendsFlow.Controls.Add(BuildTrendItem(
                    $"{top.Category} is your top category",
                    $"You spent {topPercent:0}% of your time in {top.Category.ToLowerInvariant()} apps ({FormatDuration(topDuration)})."));
            }

            // 3. Average session length for the period
            if (periodSessions.Count > 0)
            {
                var avgMinutes = Math.Max(1, (int)Math.Round(
                    periodSessions.Average(s => Math.Max(0, (s.EndTime - s.StartTime).TotalMinutes))));
                insightsTrendsFlow.Controls.Add(BuildTrendItem(
                    $"Your average session is {avgMinutes} minute{(avgMinutes == 1 ? "" : "s")}",
                    $"Across {periodSessions.Count} session{(periodSessions.Count == 1 ? "" : "s")} {periodLabel}."));
            }

            // 4. Most used app within the period
            var topAppGroup = periodSessions
                .GroupBy(s => s.AppId)
                .Select(g => (
                    App: allApps.FirstOrDefault(a => a.Id == g.Key),
                    Ticks: g.Sum(s => Math.Max(0, (s.EndTime - s.StartTime).Ticks))))
                .Where(x => x.App != null && x.Ticks > 0)
                .OrderByDescending(x => x.Ticks)
                .FirstOrDefault();

            if (topAppGroup.App != null && periodTicks > 0)
            {
                var appPercent = topAppGroup.Ticks * 100.0 / periodTicks;
                insightsTrendsFlow.Controls.Add(BuildTrendItem(
                    $"{topAppGroup.App.Name} is your most used app",
                    $"{FormatDuration(TimeSpan.FromTicks(topAppGroup.Ticks))} ({appPercent:0}% of total time)."));
            }

            foreach (Control child in insightsTrendsFlow.Controls)
            {
                child.Width = insightsTrendsFlow.Width;
            }
        }

        private (List<AppSession> Sessions, string Label) GetPreviousPeriodSessions(string period)
        {
            var today = DateTime.Today;
            return period switch
            {
                "This Month" => (
                    sessions.Where(s =>
                    {
                        var prevMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
                        var thisMonth = new DateTime(today.Year, today.Month, 1);
                        return s.StartTime.Date >= prevMonth && s.StartTime.Date < thisMonth;
                    }).ToList(),
                    "last month"),
                "Last 3 Months" => (
                    sessions.Where(s =>
                    {
                        var end = new DateTime(today.Year, today.Month, 1).AddMonths(-2);
                        var start = end.AddMonths(-3);
                        return s.StartTime.Date >= start && s.StartTime.Date < end;
                    }).ToList(),
                    "the prior 3 months"),
                _ => (
                    sessions.Where(s =>
                    {
                        var thisWeekStart = StartOfWeek(today);
                        var lastWeekStart = thisWeekStart.AddDays(-7);
                        return s.StartTime.Date >= lastWeekStart && s.StartTime.Date < thisWeekStart;
                    }).ToList(),
                    "last week")
            };
        }

        private Control BuildTrendItem(string title, string detail)
        {
            var contentWidth = Math.Max(160, insightsTrendsFlow.Width);

            var titleLabel = new Label
            {
                Text = title,
                AutoSize = true,
                Location = new Point(0, 0),
                Font = AppTheme.CardTitle,
                ForeColor = AppTheme.TextPrimary,
                MaximumSize = new Size(contentWidth, 0)
            };

            var detailLabel = new Label
            {
                Text = detail,
                AutoSize = true,
                Location = new Point(0, 20),
                Font = AppTheme.SmallText,
                ForeColor = AppTheme.TextSecondary,
                MaximumSize = new Size(contentWidth, 0)
            };

            // Measure preferred heights so wrapped detail text is fully visible.
            var titleHeight = titleLabel.PreferredHeight;
            detailLabel.Location = new Point(0, titleHeight + 2);
            var detailHeight = detailLabel.PreferredHeight;

            var row = new Panel
            {
                Width = contentWidth,
                Height = titleHeight + detailHeight + 6,
                Margin = new Padding(0, 0, 0, 8)
            };
            row.Controls.Add(titleLabel);
            row.Controls.Add(detailLabel);

            // Keep labels in sync when the flow panel is resized.
            row.Resize += (_, _) =>
            {
                var w = Math.Max(160, row.Width);
                titleLabel.MaximumSize = new Size(w, 0);
                detailLabel.MaximumSize = new Size(w, 0);
                detailLabel.Location = new Point(0, titleLabel.PreferredHeight + 2);
                row.Height = titleLabel.PreferredHeight + detailLabel.PreferredHeight + 6;
            };

            return row;
        }

        // Top 5 apps by tracked time within the selected Insights period.
        private void PopulateMostUsedApplications(List<AppSession> periodSessions)
        {
            mostUsedAppsFlow.Controls.Clear();

            // Ensure the flow panel already has a sensible width before creating rows
            // so percentage-based columns expand across the full container.
            if (mostUsedAppsFlow.Parent != null)
            {
                mostUsedAppsFlow.Width = Math.Max(200, mostUsedAppsFlow.Parent.Width - 32);
            }

            var totalsByApp = periodSessions
                .GroupBy(s => s.AppId)
                .Select(g => (
                    App: allApps.FirstOrDefault(a => a.Id == g.Key),
                    Duration: TimeSpan.FromTicks(g.Sum(s => Math.Max(0, (s.EndTime - s.StartTime).Ticks)))))
                .Where(x => x.App != null && x.Duration > TimeSpan.Zero)
                .OrderByDescending(x => x.Duration)
                .ToList();

            var totalTrackedTicks = totalsByApp.Sum(x => x.Duration.Ticks);

            if (totalsByApp.Count == 0)
            {
                mostUsedAppsFlow.Controls.Add(new Label
                {
                    Text = "No usage tracked for this period.",
                    AutoSize = true,
                    Font = AppTheme.Base,
                    ForeColor = AppTheme.TextSecondary
                });
                return;
            }

            var topApps = totalsByApp.Take(5).ToList();
            for (var i = 0; i < topApps.Count; i++)
            {
                var entry = topApps[i];
                var percent = totalTrackedTicks > 0 ? entry.Duration.Ticks * 100.0 / totalTrackedTicks : 0;
                mostUsedAppsFlow.Controls.Add(BuildMostUsedAppRow(entry.App!, i + 1, percent, entry.Duration));
            }

            // Force every row to the full flow width after they are added.
            foreach (Control child in mostUsedAppsFlow.Controls)
            {
                child.Width = mostUsedAppsFlow.Width;
            }
        }

        private Control BuildMostUsedAppRow(AppEntry app, int rank, double percentOfTotal, TimeSpan? periodDuration = null)
        {
            var contentWidth = Math.Max(280, mostUsedAppsFlow.Width);
            var displayDuration = periodDuration ?? app.TotalUsageTime;

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 1,
                // Prevent the table from collapsing its percentage columns
                // when the parent flow panel is still laying out.
                MinimumSize = new Size(280, 40)
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            content.Controls.Add(new Label
            {
                Text = rank.ToString(),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = AppTheme.SmallText,
                ForeColor = AppTheme.TextSecondary
            }, 0, 0);

            // Fixed-size icon centered in its cell so it never clips against the
            // row bounds or the neighbouring columns.
            const int iconSize = 24;
            var iconBox = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };
            var iconPicture = new PictureBox
            {
                Image = AppIconCache.GetIcon(app, iconSize),
                SizeMode = PictureBoxSizeMode.CenterImage,
                Size = new Size(iconSize, iconSize),
                BackColor = Color.Transparent
            };
            iconBox.Controls.Add(iconPicture);
            iconBox.Resize += (_, _) =>
            {
                iconPicture.Location = new Point(
                    Math.Max(0, (iconBox.ClientSize.Width - iconSize) / 2),
                    Math.Max(0, (iconBox.ClientSize.Height - iconSize) / 2));
            };
            content.Controls.Add(iconBox, 1, 0);

            var nameCell = new Panel { Dock = DockStyle.Fill };
            nameCell.Controls.Add(new Label
            {
                Text = app.Name,
                AutoSize = true,
                Location = new Point(0, 4),
                Font = AppTheme.CardTitle,
                ForeColor = AppTheme.TextPrimary
            });
            nameCell.Controls.Add(new Label
            {
                Text = app.Category,
                AutoSize = true,
                Location = new Point(0, 24),
                Font = AppTheme.SmallText,
                ForeColor = AppTheme.TextSecondary
            });
            content.Controls.Add(nameCell, 2, 0);

            content.Controls.Add(new Label
            {
                Text = FormatDuration(displayDuration),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = AppTheme.Base,
                ForeColor = AppTheme.TextSecondary
            }, 3, 0);

            content.Controls.Add(new Label
            {
                Text = $"{percentOfTotal:0}%",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = AppTheme.Base,
                ForeColor = AppTheme.TextSecondary
            }, 4, 0);

            // Inline bar, colour-coded by category, sitting on the same line as the
            // rest of the row rather than dropped below it.
            var usageBar = new UsageBar
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(8, 0, 0, 0),
                FillColor = AppTheme.CategoryColor(app.Category)
            };
            usageBar.SetPercent(percentOfTotal);
            content.Controls.Add(usageBar, 5, 0);

            // Wrapper with an inset hairline under the row (same treatment as
            // the Sessions list) so dividers don't run edge-to-edge.
            var wrapper = new Panel
            {
                Width = contentWidth,
                Height = 48,
                Margin = new Padding(0, 0, 0, 2),
                MinimumSize = new Size(280, 48)
            };
            wrapper.Controls.Add(content);

            var divider = new Panel
            {
                Height = 1,
                BackColor = AppTheme.Border,
                Dock = DockStyle.Fill
            };
            var dividerHost = new Panel
            {
                Height = 1,
                Dock = DockStyle.Bottom,
                Padding = new Padding(4, 0, 4, 0)
            };
            dividerHost.Controls.Add(divider);
            wrapper.Controls.Add(dividerHost);

            return wrapper;
        }

        // Placeholder shell for a section whose content comes in a later commit -
        // heading only, correctly positioned, nothing else yet.
        private RoundedPanel BuildEmptyInsightsContainer(string title, Padding margin)
        {
            var box = new RoundedPanel { Dock = DockStyle.Fill, Margin = margin };

            box.Controls.Add(new Label
            {
                Text = title,
                AutoSize = true,
                Location = new Point(16, 14),
                Font = AppTheme.SectionHeading,
                ForeColor = AppTheme.TextPrimary
            });

            return box;
        }

        // Details view matching the profile mock: header + running status card,
        // Today / This Week / Sessions stats, session history card with day groups.
        private Panel BuildDetailsPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.Background,
                Padding = new Padding(28, 16, 28, 20)
            };

            var topSection = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 150 + 88,
                ColumnCount = 1,
                RowCount = 2
            };
            topSection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            topSection.RowStyles.Add(new RowStyle(SizeType.Absolute, 150f));
            topSection.RowStyles.Add(new RowStyle(SizeType.Absolute, 88f));

            topSection.Controls.Add(BuildDetailsSummary(), 0, 0);
            topSection.Controls.Add(BuildDetailsStatsRow(), 0, 1);

            // Session history card: table shrink-wraps to rows; card scrolls when needed
            // so there is no empty band under the last session.
            var historyCard = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 12, 0, 0),
                Padding = new Padding(16, 12, 16, 12),
                AutoScroll = true
            };

            var historyHeader = BuildDetailsHistoryHeader();
            historyHeader.Dock = DockStyle.Top;
            historyHeader.Height = 36;

            detailsSessionsTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Padding = new Padding(0, 4, 0, 0),
                BackColor = AppTheme.CardBackground
            };
            detailsSessionsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            historyCard.Controls.Add(detailsSessionsTable);
            historyCard.Controls.Add(historyHeader);

            panel.Controls.Add(historyCard);
            panel.Controls.Add(topSection);

            return panel;
        }

        private Panel BuildDetailsHistoryHeader()
        {
            var header = new Panel { Dock = DockStyle.Fill };

            header.Controls.Add(new Label
            {
                Text = "Recent Sessions",
                AutoSize = true,
                Location = new Point(0, 8),
                Font = AppTheme.SectionHeading,
                ForeColor = AppTheme.TextPrimary
            });

            detailsViewAllSessionsLink = new Label
            {
                Text = "View all sessions →",
                AutoSize = true,
                Cursor = Cursors.Hand,
                Font = AppTheme.Base,
                ForeColor = AppTheme.Accent,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            detailsViewAllSessionsLink.Click += (_, _) => OpenSessionsForCurrentApp();
            header.Controls.Add(detailsViewAllSessionsLink);
            header.Resize += (_, _) =>
            {
                detailsViewAllSessionsLink.Location = new Point(
                    Math.Max(0, header.ClientSize.Width - detailsViewAllSessionsLink.Width),
                    8);
            };

            return header;
        }

        // Jump to the Sessions page with this app pre-selected in the filter.
        private void OpenSessionsForCurrentApp()
        {
            if (currentDetailsApp is not null)
            {
                selectedHistoryAppFilter = currentDetailsApp.Id;
                selectedHistoryCategoryFilter = null;
                selectedHistoryPeriod = "This Week";
            }

            NavigateToViewKey("History");
        }

        // Back link, app icon, name, category, total time, and the running-status
        // card with Start / Stop Tracking on the right.
        private Panel BuildDetailsSummary()
        {
            var summary = new Panel { Dock = DockStyle.Fill };

            detailsBackLink = new Label
            {
                AutoSize = true,
                Cursor = Cursors.Hand,
                Location = new Point(0, 0),
                Font = AppTheme.Base,
                ForeColor = AppTheme.Accent
            };
            detailsBackLink.Click += (_, _) =>
            {
                if (selectedSidebarItem is not null)
                {
                    SelectSidebarItem(selectedSidebarItem);
                }
            };
            summary.Controls.Add(detailsBackLink);

            const int iconSize = 48;
            detailsIconPicture = new PictureBox
            {
                Size = new Size(iconSize, iconSize),
                Location = new Point(0, 28),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            summary.Controls.Add(detailsIconPicture);

            const int textLeft = iconSize + 14;

            detailsNameLabel = new Label
            {
                AutoSize = true,
                Location = new Point(textLeft, 26),
                Font = AppTheme.Heading,
                ForeColor = AppTheme.TextPrimary
            };
            summary.Controls.Add(detailsNameLabel);

            detailsCategoryPill = new Label
            {
                AutoSize = true,
                Location = new Point(textLeft, 56),
                Font = new Font(AppTheme.SmallText, FontStyle.Bold),
                ForeColor = AppTheme.Accent,
                BackColor = AppTheme.AccentSubtle,
                Padding = new Padding(8, 3, 8, 3)
            };
            summary.Controls.Add(detailsCategoryPill);

            detailsUsageValueLabel = new Label
            {
                AutoSize = true,
                Location = new Point(textLeft, 88),
                Font = AppTheme.StatValue,
                ForeColor = AppTheme.TextPrimary
            };
            summary.Controls.Add(detailsUsageValueLabel);
            summary.Controls.Add(new Label
            {
                Text = "Total tracked time",
                AutoSize = true,
                Location = new Point(textLeft, 120),
                Font = AppTheme.SmallText,
                ForeColor = AppTheme.TextSecondary
            });

            detailsStatusPanel = BuildDetailsStatusPanel();
            detailsStatusPanel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            summary.Controls.Add(detailsStatusPanel);
            summary.Resize += (_, _) => PositionDetailsStatusPanel(summary);
            PositionDetailsStatusPanel(summary);

            return summary;
        }

        private Panel BuildDetailsStatusPanel()
        {
            var panel = new RoundedPanel
            {
                Size = new Size(220, 110),
                Padding = new Padding(14, 12, 14, 12)
            };

            detailsStatusTitleLabel = new Label
            {
                AutoSize = true,
                Location = new Point(14, 12),
                Font = new Font(AppTheme.Base, FontStyle.Bold),
                ForeColor = AppTheme.Success
            };
            panel.Controls.Add(detailsStatusTitleLabel);

            detailsStatusMetaLabel = new Label
            {
                AutoSize = true,
                Location = new Point(14, 34),
                Font = AppTheme.SmallText,
                ForeColor = AppTheme.TextSecondary
            };
            panel.Controls.Add(detailsStatusMetaLabel);

            detailsActionButton = new Button
            {
                Size = new Size(192, 36),
                Location = new Point(14, 58),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                Font = new Font(AppTheme.Base, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            detailsActionButton.FlatAppearance.BorderSize = 0;
            detailsActionButton.Click += (_, _) =>
            {
                if (currentDetailsApp is null)
                {
                    return;
                }

                if (IsProcessRunning(currentDetailsApp))
                {
                    StopApplication(currentDetailsApp);
                }
                else
                {
                    LaunchApplication(currentDetailsApp);
                }
            };
            detailsActionButton.MouseEnter += (_, _) =>
            {
                detailsActionButtonHovered = true;
                UpdateDetailsActionButtonAppearance();
            };
            detailsActionButton.MouseLeave += (_, _) =>
            {
                detailsActionButtonHovered = false;
                UpdateDetailsActionButtonAppearance();
            };
            panel.Controls.Add(detailsActionButton);

            return panel;
        }

        private void PositionDetailsStatusPanel(Control summary)
        {
            if (detailsStatusPanel is null)
            {
                return;
            }

            detailsStatusPanel.Location = new Point(
                Math.Max(0, summary.ClientSize.Width - detailsStatusPanel.Width),
                24);
        }

        private TableLayoutPanel BuildDetailsStatsRow()
        {
            var statsRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1
            };
            statsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            statsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            statsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            statsRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            AddStatCard(statsRow, "Today", "—", 0, 3, out detailsTodayValueLabel);
            AddStatCard(statsRow, "This Week", "—", 1, 3, out detailsThisWeekValueLabel);
            AddStatCard(statsRow, "Sessions", "—", 2, 3, out detailsSessionsCountValueLabel);

            return statsRow;
        }

        private void UpdateDetailsActionButtonAppearance()
        {
            if (detailsActionButton is null || detailsStatusPanel is null)
            {
                return;
            }

            var isRunning = currentDetailsApp is not null && IsProcessRunning(currentDetailsApp);

            if (isRunning)
            {
                detailsStatusPanel.BackColor = AppTheme.SuccessSubtle;
                detailsStatusTitleLabel.Text = "●  Currently Running";
                detailsStatusTitleLabel.ForeColor = AppTheme.Success;

                if (currentDetailsApp is not null
                    && activeSessionStarts.TryGetValue(currentDetailsApp.Id, out var started))
                {
                    var elapsed = DateTime.Now - started;
                    detailsStatusMetaLabel.Text =
                        $"Started {started:HH:mm}  ·  {FormatDuration(elapsed)}";
                }
                else
                {
                    detailsStatusMetaLabel.Text = "Tracking now";
                }

                // Same Start / Running / Stop pattern as library cards.
                if (detailsActionButtonHovered)
                {
                    detailsActionButton.Text = "Stop";
                    detailsActionButton.BackColor = AppTheme.Danger;
                }
                else
                {
                    detailsActionButton.Text = "Running";
                    detailsActionButton.BackColor = AppTheme.Accent;
                }
            }
            else
            {
                detailsStatusPanel.BackColor = AppTheme.PanelBackground;
                detailsStatusTitleLabel.Text = "Not running";
                detailsStatusTitleLabel.ForeColor = AppTheme.TextSecondary;
                detailsStatusMetaLabel.Text = "Launch to start tracking";
                detailsActionButton.Text = "Start";
                detailsActionButton.BackColor = AppTheme.Success;
            }
        }

        private void ShowAppDetails(AppEntry app)
        {
            currentDetailsApp = app;

            detailsBackLink.Text = selectedSidebarItem is not null
                ? $"← Back to {selectedSidebarItem.DisplayText}"
                : "← Back";

            detailsIconPicture.Image = AppIconCache.GetIcon(app, 48);
            detailsNameLabel.Text = app.Name;
            detailsCategoryPill.Text = string.IsNullOrWhiteSpace(app.Category) ? "Other" : app.Category;
            detailsUsageValueLabel.Text = FormatDuration(app.TotalUsageTime);

            UpdateDetailsStats(app);
            UpdateDetailsActionButtonAppearance();
            RefreshDetailsSessionList(app);

            libraryPanel.Visible = false;
            overviewPanel.Visible = false;
            usagePanel.Visible = false;
            historyPanel.Visible = false;
            detailsPanel.Visible = true;
        }

        private void UpdateDetailsStats(AppEntry app)
        {
            var today = DateTime.Today;
            var weekStart = StartOfWeek(today);

            var appSessions = GetAppSessionsIncludingActive(app.Id).ToList();

            var todayTicks = appSessions
                .Where(s => s.StartTime.Date == today)
                .Sum(s => Math.Max(0, (s.EndTime - s.StartTime).Ticks));
            var weekTicks = appSessions
                .Where(s => s.StartTime.Date >= weekStart && s.StartTime.Date <= today)
                .Sum(s => Math.Max(0, (s.EndTime - s.StartTime).Ticks));

            detailsTodayValueLabel.Text = FormatDuration(TimeSpan.FromTicks(todayTicks));
            detailsThisWeekValueLabel.Text = FormatDuration(TimeSpan.FromTicks(weekTicks));
            detailsSessionsCountValueLabel.Text = appSessions.Count.ToString();
        }

        private void RefreshDetailsSessionList(AppEntry app)
        {
            // Only the most recent sessions on the profile — full list is on Sessions.
            var appSessions = GetAppSessionsIncludingActive(app.Id)
                .OrderByDescending(s => s.StartTime)
                .Take(DetailsRecentSessionCount);
            PopulateDetailsSessionHistory(detailsSessionsTable, appSessions, app);
        }

        // Completed sessions for an app, plus a live open session if it is running.
        private IEnumerable<AppSession> GetAppSessionsIncludingActive(Guid appId)
        {
            var completed = sessions.Where(s => s.AppId == appId);
            if (activeSessionStarts.TryGetValue(appId, out var startTime))
            {
                var live = new AppSession
                {
                    AppId = appId,
                    StartTime = startTime,
                    EndTime = DateTime.Now
                };
                return completed.Concat(new[] { live });
            }

            return completed;
        }

        // Day-grouped session list for the profile history card (richer than the
        // plain History helper used elsewhere).
        private void PopulateDetailsSessionHistory(
            TableLayoutPanel table,
            IEnumerable<AppSession> sessionsToShow,
            AppEntry app)
        {
            table.SuspendLayout();
            table.Controls.Clear();
            table.RowStyles.Clear();
            table.RowCount = 0;

            var sessionList = sessionsToShow.ToList();
            if (sessionList.Count == 0)
            {
                table.RowCount = 1;
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
                table.Controls.Add(new Label
                {
                    Text = "No recent sessions for this app.",
                    AutoSize = true,
                    Font = AppTheme.Base,
                    ForeColor = AppTheme.TextSecondary
                }, 0, 0);
                table.ResumeLayout(true);
                table.Height = 36 + table.Padding.Vertical;
                return;
            }

            var today = DateTime.Today;
            var rowIndex = 0;

            foreach (var group in sessionList.GroupBy(s => s.StartTime.Date).OrderByDescending(g => g.Key))
            {
                var daySessions = group.OrderByDescending(s => s.StartTime).ToList();
                var dayTicks = daySessions.Sum(s => Math.Max(0, (s.EndTime - s.StartTime).Ticks));
                var dayTotal = FormatDuration(TimeSpan.FromTicks(dayTicks));

                var groupLabel = group.Key == today
                    ? "Today"
                    : group.Key == today.AddDays(-1)
                        ? "Yesterday"
                        : group.Key.ToString("ddd, dd MMM");

                table.RowCount = rowIndex + 1;
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
                table.Controls.Add(
                    BuildDetailsDayHeader(groupLabel, daySessions.Count, dayTotal),
                    0,
                    rowIndex);
                rowIndex++;

                foreach (var session in daySessions)
                {
                    table.RowCount = rowIndex + 1;
                    table.RowStyles.Add(new RowStyle(SizeType.Absolute, SessionRowHeight));
                    var row = BuildDetailsSessionRow(session, app);
                    row.Dock = DockStyle.Fill;
                    table.Controls.Add(row, 0, rowIndex);
                    rowIndex++;
                }
            }

            table.ResumeLayout(true);

            // Shrink-wrap so the table does not stretch and leave blank space under the last row.
            var contentHeight = 0;
            foreach (RowStyle style in table.RowStyles)
            {
                if (style.SizeType == SizeType.Absolute)
                {
                    contentHeight += (int)style.Height;
                }
            }

            table.Height = Math.Max(contentHeight + table.Padding.Vertical, 1);
            table.Parent?.PerformLayout();
        }

        private Control BuildDetailsDayHeader(string title, int sessionCount, string totalDuration)
        {
            var header = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.AccentSubtle
            };

            header.Controls.Add(new Label
            {
                Text = title,
                AutoSize = true,
                Location = new Point(10, 8),
                Font = AppTheme.SectionHeading,
                ForeColor = AppTheme.TextPrimary,
                BackColor = Color.Transparent
            });

            header.Controls.Add(new Label
            {
                Text = $"{sessionCount} sessions  ·  {totalDuration}",
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(200, 10),
                Font = AppTheme.SmallText,
                ForeColor = AppTheme.TextSecondary,
                BackColor = Color.Transparent
            });

            header.Resize += (_, _) =>
            {
                if (header.Controls.Count > 1)
                {
                    var summary = header.Controls[1];
                    summary.Location = new Point(
                        Math.Max(120, header.ClientSize.Width - summary.Width - 10),
                        10);
                }
            };

            return header;
        }

        private Control BuildDetailsSessionRow(AppSession session, AppEntry app)
        {
            var duration = FormatDuration(session.EndTime - session.StartTime);
            var timeRange = $"{session.StartTime:HH:mm} – {session.EndTime:HH:mm}";
            var category = string.IsNullOrWhiteSpace(app.Category) ? "Other" : app.Category;

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                Padding = new Padding(0, 0, 0, 1)
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12f));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            const int iconSize = 22;
            var iconBox = new Panel { Dock = DockStyle.Fill };
            iconBox.Controls.Add(new PictureBox
            {
                Image = AppIconCache.GetIcon(app, iconSize),
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(iconSize, iconSize),
                Location = new Point(4, 13),
                BackColor = Color.Transparent
            });
            content.Controls.Add(iconBox, 0, 0);

            var nameCell = new Panel { Dock = DockStyle.Fill };
            nameCell.Controls.Add(new Label
            {
                Text = app.Name,
                AutoSize = true,
                Location = new Point(0, 6),
                Font = AppTheme.CardTitle,
                ForeColor = AppTheme.TextPrimary
            });
            nameCell.Controls.Add(new Label
            {
                Text = category,
                AutoSize = true,
                Location = new Point(0, 26),
                Font = AppTheme.SmallText,
                ForeColor = AppTheme.TextSecondary
            });
            content.Controls.Add(nameCell, 1, 0);

            content.Controls.Add(new Label
            {
                Text = timeRange,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = AppTheme.Base,
                ForeColor = AppTheme.TextSecondary
            }, 2, 0);

            content.Controls.Add(new Label
            {
                Text = duration,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = AppTheme.Base,
                ForeColor = AppTheme.TextPrimary
            }, 3, 0);

            content.Controls.Add(new Label
            {
                Text = "›",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(AppTheme.CardTitle.FontFamily, 12f),
                ForeColor = AppTheme.TextSecondary
            }, 4, 0);

            var wrapper = new Panel { Dock = DockStyle.Fill };
            wrapper.Controls.Add(content);
            wrapper.Controls.Add(new Panel
            {
                Height = 1,
                BackColor = AppTheme.Border,
                Dock = DockStyle.Bottom
            });

            return wrapper;
        }

        private Panel BuildHistoryPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.Background,
                Padding = new Padding(28, 16, 28, 20)
            };

            // Title/subtitle, period tabs + filters, and stat cards are stacked via
            // explicit (column, row) placement. The session list lives in its own
            // bordered card below (heading + rows), matching the design mock.
            // Stat cards row matches Insights summary height (80) so the three cards
            // read the same size on both pages.
            var topSection = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 56 + 48 + 80,
                ColumnCount = 1,
                RowCount = 3
            };
            topSection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            topSection.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
            topSection.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
            topSection.RowStyles.Add(new RowStyle(SizeType.Absolute, 80f));

            topSection.Controls.Add(BuildSessionsPageTitle(), 0, 0);
            topSection.Controls.Add(BuildPeriodAndFiltersRow(), 0, 1);
            topSection.Controls.Add(BuildSessionsStatsRow(), 0, 2);

            // Bordered card wrapping "Today's Sessions" header + the list.
            // The card scrolls when the list is long; the table itself shrink-wraps
            // to its rows so there is no empty band under "Load More".
            var sessionsCard = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(16, 12, 16, 12),
                AutoScroll = true
            };

            var cardHeader = BuildSessionsSummaryRow();
            cardHeader.Dock = DockStyle.Top;
            cardHeader.Height = 32;

            historyTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Padding = new Padding(0),
                BackColor = AppTheme.CardBackground
            };
            historyTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            // Fill before Top so Dock layout leaves the header strip and fills the rest.
            sessionsCard.Controls.Add(historyTable);
            sessionsCard.Controls.Add(cardHeader);

            // Fill-docked card added before the Top-docked section - same ordering as
            // BuildLibraryArea/the Details view.
            panel.Controls.Add(sessionsCard);
            panel.Controls.Add(topSection);

            PopulateHistory();

            return panel;
        }

        private Panel BuildSessionsPageTitle()
        {
            var titlePanel = new Panel { Dock = DockStyle.Fill };

            titlePanel.Controls.Add(new Label
            {
                Text = "Sessions",
                AutoSize = true,
                Location = new Point(0, 0),
                Font = AppTheme.Heading,
                ForeColor = AppTheme.TextPrimary
            });

            titlePanel.Controls.Add(new Label
            {
                Text = "View and manage your application usage sessions.",
                AutoSize = true,
                Location = new Point(0, 28),
                Font = AppTheme.Base,
                ForeColor = AppTheme.TextSecondary
            });

            return titlePanel;
        }

        // Period tabs on the left; application + category filter dropdowns on the right
        // (matches the Sessions redesign mock). Custom date range is still a
        // separate task.
        private Panel BuildPeriodAndFiltersRow()
        {
            var row = new Panel { Dock = DockStyle.Fill };

            var tabs = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 4, 0, 0)
            };

            foreach (var period in new[] { "Today", "This Week", "This Month" })
            {
                var tabButton = new Button
                {
                    Text = period,
                    FlatStyle = FlatStyle.Flat,
                    Size = new Size(94, 32),
                    Margin = new Padding(0, 0, 8, 0),
                    Font = AppTheme.SmallText,
                    Cursor = Cursors.Hand,
                    TabStop = false
                };
                tabButton.FlatAppearance.BorderSize = 0;

                var capturedPeriod = period;
                tabButton.Click += (_, _) =>
                {
                    if (selectedHistoryPeriod == capturedPeriod)
                    {
                        return;
                    }

                    selectedHistoryPeriod = capturedPeriod;
                    PopulateHistory();
                };

                periodTabButtons[period] = tabButton;
                tabs.Controls.Add(tabButton);
            }

            var filters = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 4, 0, 0)
            };

            historyAppFilterCombo = CreateSessionsFilterCombo(180);
            historyAppFilterCombo.SelectedIndexChanged += (_, _) =>
            {
                if (suppressHistoryFilterEvents)
                {
                    return;
                }

                if (historyAppFilterCombo.SelectedItem is FilterOption option)
                {
                    selectedHistoryAppFilter = option.AppId;
                    PopulateHistory();
                }
            };

            historyCategoryFilterCombo = CreateSessionsFilterCombo(160);
            historyCategoryFilterCombo.SelectedIndexChanged += (_, _) =>
            {
                if (suppressHistoryFilterEvents)
                {
                    return;
                }

                if (historyCategoryFilterCombo.SelectedItem is FilterOption option)
                {
                    selectedHistoryCategoryFilter = option.Category;
                    PopulateHistory();
                }
            };

            filters.Controls.Add(historyAppFilterCombo);
            filters.Controls.Add(historyCategoryFilterCombo);

            // Right-docked filters first so Left tabs keep the remaining space cleanly.
            row.Controls.Add(filters);
            row.Controls.Add(tabs);

            RefreshSessionFilterOptions();

            return row;
        }

        private static ComboBox CreateSessionsFilterCombo(int width)
        {
            return new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Width = width,
                Height = 32,
                Margin = new Padding(8, 0, 0, 0),
                Font = AppTheme.Base,
                BackColor = AppTheme.CardBackground,
                ForeColor = AppTheme.TextPrimary
            };
        }

        // Display text + optional app/category keys for the Sessions filter combos.
        private sealed class FilterOption
        {
            public string Text { get; }
            public Guid? AppId { get; }
            public string? Category { get; }

            public FilterOption(string text, Guid? appId = null, string? category = null)
            {
                Text = text;
                AppId = appId;
                Category = category;
            }

            public override string ToString() => Text;
        }

        private void RefreshSessionFilterOptions()
        {
            if (historyAppFilterCombo is null || historyCategoryFilterCombo is null)
            {
                return;
            }

            // Preserve current selection while rebuilding items.
            var previousAppId = selectedHistoryAppFilter;
            var previousCategory = selectedHistoryCategoryFilter;

            suppressHistoryFilterEvents = true;
            historyAppFilterCombo.BeginUpdate();
            historyCategoryFilterCombo.BeginUpdate();
            try
            {
                historyAppFilterCombo.Items.Clear();
                historyAppFilterCombo.Items.Add(new FilterOption("All Applications"));
                foreach (var app in allApps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
                {
                    historyAppFilterCombo.Items.Add(new FilterOption(app.Name, appId: app.Id));
                }

                historyCategoryFilterCombo.Items.Clear();
                historyCategoryFilterCombo.Items.Add(new FilterOption("All Categories"));
                foreach (var category in allApps
                    .Select(a => string.IsNullOrWhiteSpace(a.Category) ? "Other" : a.Category)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
                {
                    historyCategoryFilterCombo.Items.Add(new FilterOption(category, category: category));
                }

                // App: match by id; null id is the "All Applications" entry.
                SelectFilterOption(historyAppFilterCombo, o => o.AppId == previousAppId);
                // Category: match by name; null is the "All Categories" entry.
                SelectFilterOption(historyCategoryFilterCombo, o =>
                    string.Equals(o.Category, previousCategory, StringComparison.OrdinalIgnoreCase));

                // Keep filter fields in sync with whatever ended up selected (e.g. if an
                // app was removed from the library).
                if (historyAppFilterCombo.SelectedItem is FilterOption appOption)
                {
                    selectedHistoryAppFilter = appOption.AppId;
                }

                if (historyCategoryFilterCombo.SelectedItem is FilterOption categoryOption)
                {
                    selectedHistoryCategoryFilter = categoryOption.Category;
                }
            }
            finally
            {
                historyAppFilterCombo.EndUpdate();
                historyCategoryFilterCombo.EndUpdate();
                suppressHistoryFilterEvents = false;
            }
        }

        private static void SelectFilterOption(ComboBox combo, Func<FilterOption, bool> match)
        {
            for (var i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is FilterOption option && match(option))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }

            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }
        }

        private TableLayoutPanel BuildSessionsStatsRow()
        {
            var statsRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1
            };
            statsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            statsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            statsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            statsRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            AddStatCard(statsRow, "Total Sessions", "—", 0, 3, out historySessionsCountValueLabel);
            AddStatCard(statsRow, "Total Time", "—", 1, 3, out historyTotalTimeValueLabel);
            AddStatCard(statsRow, "Most Used App", "—", 2, 3, out historyMostUsedAppValueLabel);

            return statsRow;
        }

        private Panel BuildSessionsSummaryRow()
        {
            var row = new Panel { Dock = DockStyle.Fill };

            historySummaryLabel = new Label
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleRight,
                Font = AppTheme.SmallText,
                ForeColor = AppTheme.TextSecondary
            };
            row.Controls.Add(historySummaryLabel);

            historySessionsHeadingLabel = new Label
            {
                AutoSize = true,
                Location = new Point(0, 6),
                Font = AppTheme.SectionHeading,
                ForeColor = AppTheme.TextPrimary
            };
            row.Controls.Add(historySessionsHeadingLabel);

            return row;
        }

        // Rebuilds the whole Sessions page (stat cards, summary line, and the row
        // list) for the current period + application/category filters.
        private void PopulateHistory()
        {
            UpdatePeriodTabAppearance();
            RefreshSessionFilterOptions();

            historyFilteredSessions = GetSessionsForPeriod(selectedHistoryPeriod)
                .Where(MatchesHistoryFilters)
                .OrderByDescending(s => s.StartTime)
                .ToList();

            // Reset to the first page whenever the period or filters change.
            historyVisibleCount = HistoryInitialCount;

            historySessionsCountValueLabel.Text = historyFilteredSessions.Count.ToString();

            var totalTime = TimeSpan.FromTicks(historyFilteredSessions.Sum(s => Math.Max(0, (s.EndTime - s.StartTime).Ticks)));
            historyTotalTimeValueLabel.Text = FormatDuration(totalTime);

            var mostUsed = historyFilteredSessions
                .GroupBy(s => s.AppId)
                .Select(g => (
                    Name: allApps.FirstOrDefault(a => a.Id == g.Key)?.Name ?? "Removed app",
                    Total: TimeSpan.FromTicks(g.Sum(s => Math.Max(0, (s.EndTime - s.StartTime).Ticks)))))
                .OrderByDescending(x => x.Total)
                .FirstOrDefault();

            if (mostUsed.Total > TimeSpan.Zero)
            {
                historyMostUsedAppValueLabel.Text = mostUsed.Name;
            }
            else
            {
                historyMostUsedAppValueLabel.Text = "—";
            }

            historySessionsHeadingLabel.Text = selectedHistoryPeriod switch
            {
                "Today" => "Today's Sessions",
                "This Week" => "This Week's Sessions",
                "This Month" => "This Month's Sessions",
                _ => "Sessions"
            };
            historySummaryLabel.Text = $"{historyFilteredSessions.Count} sessions  ·  {FormatDuration(totalTime)} total";

            var emptyMessage = selectedHistoryAppFilter is not null || selectedHistoryCategoryFilter is not null
                ? "No sessions match the current filters."
                : "No sessions recorded for this period.";
            PopulateSessionsList(historyTable, emptyMessage);
        }

        private bool MatchesHistoryFilters(AppSession session)
        {
            if (selectedHistoryAppFilter is Guid appId && session.AppId != appId)
            {
                return false;
            }

            if (selectedHistoryCategoryFilter is not null)
            {
                var app = allApps.FirstOrDefault(a => a.Id == session.AppId);
                var category = string.IsNullOrWhiteSpace(app?.Category) ? "Other" : app!.Category;
                if (!string.Equals(category, selectedHistoryCategoryFilter, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<AppSession> FilterSessionsByPeriod(IEnumerable<AppSession> source, string period)
        {
            var today = DateTime.Today;
            return period switch
            {
                "Today" => source.Where(s => s.StartTime.Date == today),
                "This Week" => source.Where(s => s.StartTime.Date >= StartOfWeek(today) && s.StartTime.Date <= today),
                "This Month" => source.Where(s => s.StartTime.Date >= new DateTime(today.Year, today.Month, 1) && s.StartTime.Date <= today),
                "Last 3 Months" => source.Where(s =>
                {
                    var start = new DateTime(today.Year, today.Month, 1).AddMonths(-2);
                    return s.StartTime.Date >= start && s.StartTime.Date <= today;
                }),
                _ => source
            };
        }

        // Completed sessions for the period, plus any currently-running apps as
        // open-ended sessions so live usage is reflected on both pages.
        private IEnumerable<AppSession> GetSessionsForPeriod(string period)
        {
            var completed = FilterSessionsByPeriod(sessions, period);
            var now = DateTime.Now;
            var active = activeSessionStarts
                .Select(kvp => new AppSession
                {
                    AppId = kvp.Key,
                    StartTime = kvp.Value,
                    EndTime = now
                })
                .Where(s => FilterSessionsByPeriod(new[] { s }, period).Any());

            return completed.Concat(active);
        }

        private void UpdatePeriodTabAppearance()
        {
            foreach (var (period, button) in periodTabButtons)
            {
                var isSelected = period == selectedHistoryPeriod;
                button.BackColor = isSelected ? AppTheme.Accent : AppTheme.Background;
                button.ForeColor = isSelected ? Color.White : AppTheme.TextSecondary;
            }
        }

        // Flat list, most recent first - no day-grouping headers here (unlike
        // PopulateSessionsTable/the Details view's per-app list), since the period
        // tabs above already establish the timeframe being shown.
        // Only the first historyVisibleCount rows are created as controls; a
        // "Load More" button appends the next batch when more remain.
        private void PopulateSessionsList(TableLayoutPanel table, string emptyMessage)
        {
            table.SuspendLayout();
            table.Controls.Clear();
            table.RowStyles.Clear();
            table.RowCount = 0;

            if (historyFilteredSessions.Count == 0)
            {
                table.RowCount = 1;
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
                table.Controls.Add(new Label
                {
                    Text = emptyMessage,
                    AutoSize = true,
                    Font = AppTheme.Base,
                    ForeColor = AppTheme.TextSecondary
                }, 0, 0);
                table.ResumeLayout(true);
                table.Parent?.PerformLayout();
                return;
            }

            var showCount = Math.Min(historyVisibleCount, historyFilteredSessions.Count);

            for (var i = 0; i < showCount; i++)
            {
                table.RowCount = i + 1;
                // See SessionRowHeight constant near the top of MainForm.
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, SessionRowHeight));
                var row = BuildSessionListRow(historyFilteredSessions[i]);
                row.Dock = DockStyle.Fill;
                table.Controls.Add(row, 0, i);
            }

            if (showCount < historyFilteredSessions.Count)
            {
                var remaining = historyFilteredSessions.Count - showCount;
                var loadMoreRow = table.RowCount;
                table.RowCount = loadMoreRow + 1;
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
                table.Controls.Add(BuildLoadMoreSessionsButton(remaining), 0, loadMoreRow);
            }

            table.ResumeLayout(true);

            // Shrink-wrap height to the sum of absolute rows so the table does not
            // stretch and leave a blank band under the last row / Load More.
            var contentHeight = 0;
            foreach (RowStyle style in table.RowStyles)
            {
                if (style.SizeType == SizeType.Absolute)
                {
                    contentHeight += (int)style.Height;
                }
            }

            table.Height = Math.Max(contentHeight, 1);
            table.Parent?.PerformLayout();

            // Keep the sessions card scrolled to the top after period/filter changes.
            if (table.Parent is ScrollableControl scrollParent)
            {
                try
                {
                    scrollParent.AutoScrollPosition = new Point(0, 0);
                }
                catch
                {
                    // Handle may not exist yet during first layout.
                }
            }
        }

        private Control BuildLoadMoreSessionsButton(int remainingCount)
        {
            var button = new Button
            {
                Text = remainingCount <= HistoryPageSize
                    ? $"Load More ({remainingCount} remaining)"
                    : $"Load More (next {HistoryPageSize} of {remainingCount})",
                FlatStyle = FlatStyle.Flat,
                Height = 36,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 4, 0, 0),
                Font = AppTheme.Base,
                ForeColor = AppTheme.Accent,
                BackColor = AppTheme.AccentSubtle,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) =>
            {
                historyVisibleCount += HistoryPageSize;
                PopulateSessionsList(historyTable, "No sessions recorded for this period.");
            };
            return button;
        }

        // Name + category, time range, duration, a "Running" pill when applicable,
        // and a chevron - the whole row is clickable through to that app's Details
        // view (skipped for a session whose app has since been removed). An inset
        // hairline divider sits under the content so it doesn't run edge-to-edge.
        private Control BuildSessionListRow(AppSession session)
        {
            var app = allApps.FirstOrDefault(a => a.Id == session.AppId);
            var appName = app?.Name ?? "Removed app";
            var category = app?.Category ?? string.Empty;
            var duration = FormatDuration(session.EndTime - session.StartTime);
            var timeRange = $"{session.StartTime:HH:mm} - {session.EndTime:HH:mm}";
            var isCurrentlyRunning = app is not null && IsProcessRunning(app);

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 1,
                // Leave room at the bottom for the divider line.
                Padding = new Padding(0, 0, 0, 1)
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12f));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            const int iconSize = 22;
            var iconBox = new Panel { Dock = DockStyle.Fill };
            var iconPicture = new PictureBox
            {
                Image = AppIconCache.GetIcon(app, iconSize),
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(iconSize, iconSize),
                Location = new Point(2, 13),
                BackColor = Color.Transparent
            };
            iconBox.Controls.Add(iconPicture);
            content.Controls.Add(iconBox, 0, 0);

            // Name + category: enough depth for both lines, minimal empty pad
            // above the divider (SessionRowHeight = 52).
            var nameCell = new Panel { Dock = DockStyle.Fill };
            nameCell.Controls.Add(new Label
            {
                Text = appName,
                AutoSize = true,
                Location = new Point(0, 6),
                Font = AppTheme.CardTitle,
                ForeColor = AppTheme.TextPrimary
            });
            nameCell.Controls.Add(new Label
            {
                Text = category,
                AutoSize = true,
                Location = new Point(0, 26),
                Font = AppTheme.SmallText,
                ForeColor = AppTheme.TextSecondary
            });
            content.Controls.Add(nameCell, 1, 0);

            content.Controls.Add(new Label
            {
                Text = timeRange,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = AppTheme.Base,
                ForeColor = AppTheme.TextSecondary
            }, 2, 0);

            content.Controls.Add(new Label
            {
                Text = duration,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = AppTheme.Base,
                ForeColor = AppTheme.TextSecondary
            }, 3, 0);

            var statusCell = new Panel { Dock = DockStyle.Fill };
            if (isCurrentlyRunning)
            {
                statusCell.Controls.Add(new Label
                {
                    Text = "Running",
                    AutoSize = true,
                    Location = new Point(0, 12),
                    Font = new Font(AppTheme.SmallText, FontStyle.Bold),
                    ForeColor = AppTheme.Success,
                    BackColor = AppTheme.SuccessSubtle,
                    Padding = new Padding(8, 2, 8, 2)
                });
            }
            content.Controls.Add(statusCell, 4, 0);

            content.Controls.Add(new Label
            {
                Text = "›",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(AppTheme.CardTitle.FontFamily, 12f),
                ForeColor = AppTheme.TextSecondary
            }, 5, 0);

            // Wrapper so we can draw an inset divider that doesn't span the full width.
            var wrapper = new Panel { Dock = DockStyle.Fill };
            wrapper.Controls.Add(content);

            // Hairline under the row - inset on left/right so it doesn't run edge-to-edge.
            var divider = new Panel
            {
                Height = 1,
                BackColor = AppTheme.Border,
                Dock = DockStyle.Bottom,
                Margin = new Padding(0),
                // Dock.Bottom ignores Margin for horizontal inset; use Padding on the
                // parent via a nested panel instead.
            };
            var dividerHost = new Panel
            {
                Height = 1,
                Dock = DockStyle.Bottom,
                Padding = new Padding(4, 0, 4, 0)
            };
            divider.Dock = DockStyle.Fill;
            dividerHost.Controls.Add(divider);
            wrapper.Controls.Add(dividerHost);

            if (app is not null)
            {
                WireClickRecursively(wrapper, (_, _) => ShowAppDetails(app));
            }

            return wrapper;
        }

        // Attaches the same click handler (and hand cursor) to a control and every
        // descendant - needed because clicks on child controls don't bubble up to a
        // parent's own Click event in WinForms.
        private static void WireClickRecursively(Control control, EventHandler handler)
        {
            control.Click += handler;
            control.Cursor = Cursors.Hand;
            foreach (Control child in control.Controls)
            {
                WireClickRecursively(child, handler);
            }
        }

        // Shared by History (all sessions) and the app Details view (one app's
        // sessions) - same grouped-by-day layout either way, just a different source
        // list and empty-state message.
        private void PopulateSessionsTable(TableLayoutPanel table, IEnumerable<AppSession> sessionsToShow, string emptyMessage)
        {
            table.Controls.Clear();
            table.RowStyles.Clear();
            table.RowCount = 0;

            var sessionList = sessionsToShow.ToList();

            if (sessionList.Count == 0)
            {
                table.RowCount = 1;
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
                table.Controls.Add(new Label
                {
                    Text = emptyMessage,
                    AutoSize = true,
                    Font = AppTheme.Base,
                    ForeColor = AppTheme.TextSecondary
                }, 0, 0);
                return;
            }

            var today = DateTime.Today;
            var rowIndex = 0;

            var groups = sessionList.GroupBy(s => s.StartTime.Date).OrderByDescending(g => g.Key);

            foreach (var group in groups)
            {
                var groupLabel = group.Key == today
                    ? "Today"
                    : group.Key == today.AddDays(-1)
                        ? "Yesterday"
                        : group.Key.ToString("ddd, dd MMM");

                table.RowCount = rowIndex + 1;
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
                table.Controls.Add(new Label
                {
                    Text = groupLabel,
                    AutoSize = true,
                    Font = AppTheme.SectionHeading,
                    ForeColor = AppTheme.TextSecondary
                }, 0, rowIndex);
                rowIndex++;

                foreach (var session in group.OrderByDescending(s => s.StartTime))
                {
                    table.RowCount = rowIndex + 1;
                    table.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
                    var row = BuildHistoryRow(session);
                    row.Dock = DockStyle.Fill;
                    table.Controls.Add(row, 0, rowIndex);
                    rowIndex++;
                }
            }
        }

        private Panel BuildHistoryRow(AppSession session)
        {
            var appName = allApps.FirstOrDefault(a => a.Id == session.AppId)?.Name ?? "Removed app";
            var duration = FormatDuration(session.EndTime - session.StartTime);
            var timeRange = $"{session.StartTime:HH:mm} - {session.EndTime:HH:mm}";

            var row = new Panel();

            row.Controls.Add(new Label
            {
                Text = $"{appName}  ·  {duration}",
                AutoSize = true,
                Location = new Point(0, 4),
                Font = AppTheme.CardTitle,
                ForeColor = AppTheme.TextPrimary
            });

            row.Controls.Add(new Label
            {
                Text = timeRange,
                AutoSize = true,
                Location = new Point(0, 28),
                Font = AppTheme.SmallText,
                ForeColor = AppTheme.TextSecondary
            });

            return row;
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                return "0m";
            }

            if (duration.TotalMinutes < 1)
            {
                return "<1m";
            }

            var hours = (int)duration.TotalHours;
            var minutes = duration.Minutes;
            return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
        }

        // Number of columns in the Recent/Most Used grid - also how many apps are shown.
        private const int RecentAppsColumnCount = 5;

        private Panel BuildOverviewPanel()
        {
            // Overview layout - a row of stat cards, a Recent/Most Used row reusing
            // the existing AppCard, and a basic bar chart of today's usage per app.
            //
            // A single-column TableLayoutPanel stacks the sections top to bottom -
            // each row is Dock=Top full width, so the stat cards, recent apps grid
            // and usage chart all line up to the same width automatically.
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
            // Tall enough for AppCard (142) + a little breathing room.
            // Tall enough for AppCard (156) + gap.
            // Tall enough for AppCard (138) + gap.
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 152f));
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

            AddStatCard(statsTable, "Today", "—", 0, 3, out overviewTodayValueLabel);
            AddStatCard(statsTable, "This Week", "—", 1, 3, out overviewThisWeekValueLabel);
            AddSessionsStatCard(statsTable, 2, 3);
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
                Height = 142,
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

            var usageChartCard = new RoundedPanel
            {
                Dock = DockStyle.Top,
                Height = 140,
                Padding = new Padding(4)
            };
            usageChart = new UsageTodayChart { Dock = DockStyle.Fill };
            usageChartCard.Controls.Add(usageChart);
            layout.Controls.Add(usageChartCard, 0, 5);

            panel.Controls.Add(layout);

            PopulateOverviewRecentApps();
            UpdateOverviewTodayCard();
            UpdateOverviewThisWeekCard();
            UpdateUsageTodayChart();

            return panel;
        }

        // Adds a stat card to the given cell, with a small gap to its neighbours
        // (none on the outer edges) so the row lines up flush with the section above.
        // Callers keep the value label so its text can be refreshed with real data later.
        private void AddStatCard(
            TableLayoutPanel table,
            string label,
            string value,
            int column,
            int columnCount,
            out Label valueLabel,
            bool largeValue = false)
        {
            var card = BuildStatCard(label, value, out valueLabel, largeValue);
            card.Dock = DockStyle.Fill;
            card.Margin = GridCellMargin(column, columnCount, gap: 16);
            table.Controls.Add(card, column, 0);
        }

        // The Sessions card needs its value updated later (real data, refreshed each
        // time Overview is opened) rather than being fixed at build time like the
        // other two, so it keeps hold of the value label via overviewSessionsValueLabel.
        // It's also the only stat card that's clickable for now, since it's the only
        // one with somewhere to go - the History view.
        private void AddSessionsStatCard(TableLayoutPanel table, int column, int columnCount)
        {
            var card = BuildStatCard("Sessions", "—", out var valueLabel);
            overviewSessionsValueLabel = valueLabel;
            card.Dock = DockStyle.Fill;
            card.Margin = GridCellMargin(column, columnCount, gap: 16);
            card.Cursor = Cursors.Hand;

            void GoToHistory(object? _, EventArgs __) => NavigateToViewKey("History");
            card.Click += GoToHistory;
            foreach (Control child in card.Controls)
            {
                child.Cursor = Cursors.Hand;
                child.Click += GoToHistory;
            }

            table.Controls.Add(card, column, 0);

            UpdateOverviewSessionsCard();
        }

        // Counts completed sessions that started today. A session still in progress
        // (app currently open) isn't in the sessions list yet - see
        // activeSessionStarts - so it won't count until it ends. Good enough for now.
        private void UpdateOverviewSessionsCard()
        {
            var today = DateTime.Today;
            var sessionsToday = sessions.Count(s => s.StartTime.Date == today);
            overviewSessionsValueLabel.Text = sessionsToday.ToString();
        }

        private void UpdateOverviewTodayCard()
        {
            var today = DateTime.Today;
            var totalToday = SumSessionDurations(s => s.StartTime.Date == today);
            overviewTodayValueLabel.Text = FormatDuration(totalToday);
        }

        private void UpdateOverviewThisWeekCard()
        {
            var weekStart = StartOfWeek(DateTime.Today);
            var totalThisWeek = SumSessionDurations(s => s.StartTime.Date >= weekStart);
            overviewThisWeekValueLabel.Text = FormatDuration(totalThisWeek);
        }

        // Sums each app's completed sessions from today, one bar per app, sorted
        // biggest first. Same "completed sessions only" caveat as the other cards -
        // an app still running right now won't show up until it's closed.
        private void UpdateUsageTodayChart()
        {
            var today = DateTime.Today;

            var usageByApp = sessions
                .Where(s => s.StartTime.Date == today)
                .GroupBy(s => s.AppId)
                .Select(g => (
                    Name: allApps.FirstOrDefault(a => a.Id == g.Key)?.Name ?? "Removed app",
                    Duration: TimeSpan.FromTicks(g.Sum(s => (s.EndTime - s.StartTime).Ticks))))
                .OrderByDescending(u => u.Duration)
                .ToList();

            usageChart.SetData(usageByApp);
        }

        private TimeSpan SumSessionDurations(Func<AppSession, bool> predicate)
        {
            var total = TimeSpan.Zero;
            foreach (var session in sessions)
            {
                if (predicate(session))
                {
                    total += session.EndTime - session.StartTime;
                }
            }
            return total;
        }

        // Monday as the start of the week.
        private static DateTime StartOfWeek(DateTime date)
        {
            var daysSinceMonday = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            return date.AddDays(-daysSinceMonday).Date;
        }

        private static Padding GridCellMargin(int column, int columnCount, int gap)
        {
            var left = column == 0 ? 0 : gap / 2;
            var right = column == columnCount - 1 ? 0 : gap / 2;
            return new Padding(left, 0, right, 0);
        }

        private Panel BuildStatCard(string label, string value, out Label valueLabel, bool largeValue = false)
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

            valueLabel = new Label
            {
                Text = value,
                AutoSize = true,
                Location = new Point(16, largeValue ? 40 : 38),
                Font = largeValue ? AppTheme.StatValue : AppTheme.CardTitle,
                ForeColor = AppTheme.TextPrimary
            };
            card.Controls.Add(valueLabel);

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
                    card.StopRequested += (_, _) => StopApplication(app);
                    card.EditRequested += (_, _) => EditApplication(app);
                    card.RemoveRequested += (_, _) => RemoveApplication(app);
                    card.DetailsRequested += (_, _) => ShowAppDetails(app);
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

            var activityDivider = new Panel
            {
                Width = 180,
                Height = 1,
                Margin = new Padding(4, 12, 0, 8),
                BackColor = AppTheme.Border
            };
            flow.Controls.Add(activityDivider);

            var activityHeader = new Label
            {
                Text = "ACTIVITY",
                AutoSize = false,
                Width = 180,
                Height = 28,
                Font = new Font(AppTheme.SmallText, FontStyle.Bold),
                ForeColor = AppTheme.TextSecondary,
                Padding = new Padding(4, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };
            flow.Controls.Add(activityHeader);

            // Displayed as "Sessions" and "Insights" - ViewKey identifiers ("History",
            // "Usage") are left as-is internally to keep this change small.
            AddSidebarItem(flow, "Sessions", category: null, viewKey: "History");
            AddSidebarItem(flow, "Insights", category: null, viewKey: "Usage");

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
            if (sender is SidebarItem clicked)
            {
                SelectSidebarItem(clicked);
            }
        }

        // Used by the sidebar click handler above, and by anything else that needs to
        // jump straight to a nav destination (e.g. clicking the Sessions stat card).
        private void SelectSidebarItem(SidebarItem item)
        {
            if (selectedSidebarItem is not null)
            {
                selectedSidebarItem.IsSelected = false;
            }

            item.IsSelected = true;
            selectedSidebarItem = item;

            if (item.ViewKey is not null)
            {
                libraryPanel.Visible = false;
                overviewPanel.Visible = item.ViewKey == "Overview";
                usagePanel.Visible = item.ViewKey == "Usage";
                historyPanel.Visible = item.ViewKey == "History";
                detailsPanel.Visible = false;

                if (item.ViewKey == "Overview")
                {
                    PopulateOverviewRecentApps();
                    UpdateOverviewSessionsCard();
                    UpdateOverviewTodayCard();
                    UpdateOverviewThisWeekCard();
                    UpdateUsageTodayChart();
                }

                if (item.ViewKey == "Usage")
                {
                    PopulateInsights();
                }

                if (item.ViewKey == "History")
                {
                    PopulateHistory();
                }

                return;
            }

            overviewPanel.Visible = false;
            usagePanel.Visible = false;
            historyPanel.Visible = false;
            detailsPanel.Visible = false;
            libraryPanel.Visible = true;
            ApplyFilter();
        }

        private void NavigateToViewKey(string viewKey)
        {
            var item = sidebarItems.FirstOrDefault(i => i.ViewKey == viewKey);
            if (item is not null)
            {
                SelectSidebarItem(item);
            }
        }

        private Panel BuildLibraryArea()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.Background,
                Padding = new Padding(28, 16, 28, 20)
            };

            // Extra top padding so the header sits clearly below the suggested strip.
            var headerRow = new Panel
            {
                Dock = DockStyle.Top,
                Height = 52,
                Padding = new Padding(0, 14, 0, 0)
            };

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
            // Region needs a real size; apply after the header has laid out the button.
            headerRow.Resize += (_, _) => ApplyRoundedButtonRegion(btnAddApplication, radius: 8);
            btnAddApplication.SizeChanged += (_, _) => ApplyRoundedButtonRegion(btnAddApplication, radius: 8);

            // Fill-docked heading added before the Right-docked button, so the button
            // claims its fixed slice on the right and the heading fills the rest.
            headerRow.Controls.Add(libraryHeading);
            headerRow.Controls.Add(btnAddApplication);

            suggestedAppsSection = BuildSuggestedAppsSection();
            suggestedAppsSection.Dock = DockStyle.Top;

            libraryFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = true,
                BackColor = AppTheme.Background,
                Padding = new Padding(0, 8, 0, 0)
            };

            // Dock order: last Top control is nearest the top edge.
            // Suggested strip → YOUR APPLICATIONS header → app grid.
            panel.Controls.Add(libraryFlow);
            panel.Controls.Add(headerRow);
            panel.Controls.Add(suggestedAppsSection);

            return panel;
        }

        private Panel BuildSuggestedAppsSection()
        {
            // Blue-tinted container matching the Suggested Applications mock.
            var section = new RoundedPanel
            {
                Height = 0,
                Visible = false,
                BackColor = AppTheme.AccentSubtle,
                Padding = new Padding(16, 14, 16, 14)
            };

            section.Controls.Add(new Label
            {
                Text = "Suggested Applications",
                AutoSize = true,
                Location = new Point(16, 14),
                Font = AppTheme.SectionHeading,
                ForeColor = AppTheme.TextPrimary,
                BackColor = Color.Transparent
            });

            suggestedAppsSubtitle = new Label
            {
                Text = "Apps detected on your PC that aren't in your library yet.",
                AutoSize = true,
                Location = new Point(16, 36),
                Font = AppTheme.SmallText,
                ForeColor = AppTheme.TextSecondary,
                BackColor = Color.Transparent
            };
            section.Controls.Add(suggestedAppsSubtitle);

            suggestedAppsActionLink = new Label
            {
                Text = "Dismiss all",
                AutoSize = true,
                Cursor = Cursors.Hand,
                Font = AppTheme.Base,
                ForeColor = AppTheme.Accent,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            suggestedAppsActionLink.Click += (_, _) => DismissAllSuggestedApplications();
            section.Controls.Add(suggestedAppsActionLink);

            suggestedAppsFlow = new FlowLayoutPanel
            {
                Location = new Point(12, 54),
                Height = 122,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = false,
                // Same blue as the strip so rounded card corners blend cleanly.
                BackColor = AppTheme.AccentSubtle
            };
            section.Controls.Add(suggestedAppsFlow);

            section.Resize += (_, _) =>
            {
                suggestedAppsFlow.Width = Math.Max(0, section.ClientSize.Width - 24);
                suggestedAppsActionLink.Location = new Point(
                    Math.Max(0, section.ClientSize.Width - suggestedAppsActionLink.Width - 16),
                    16);
            };

            return section;
        }

        private void RefreshSuggestedApps()
        {
            if (suggestedAppsSection is null || suggestedAppsFlow is null)
            {
                return;
            }

            // Only show on the full All Apps library (not category filters or search).
            var showOnAllApps =
                libraryPanel.Visible
                && selectedSidebarItem?.ViewKey is null
                && selectedSidebarItem?.FilterCategory is null
                && string.IsNullOrWhiteSpace(txtSearch.Text);

            if (!showOnAllApps)
            {
                suggestedAppsSection.Visible = false;
                suggestedAppsSection.Height = 0;
                return;
            }

            List<AppDetector.DetectedApp> suggestions;
            try
            {
                suggestions = AppDetector.DetectSuggestions(allApps, ignoredSuggestions, maxCount: 6).ToList();
            }
            catch
            {
                suggestions = new List<AppDetector.DetectedApp>();
            }

            suggestedAppsFlow.SuspendLayout();
            suggestedAppsFlow.Controls.Clear();

            if (suggestions.Count == 0)
            {
                // Dismiss all / ignore left nothing to show — offer restore instead of vanishing.
                if (ignoredSuggestions.Count > 0)
                {
                    ShowSuggestionsRestoredPrompt();
                    suggestedAppsFlow.ResumeLayout();
                    return;
                }

                suggestedAppsSection.Visible = false;
                suggestedAppsSection.Height = 0;
                suggestedAppsFlow.ResumeLayout();
                return;
            }

            foreach (var detected in suggestions)
            {
                suggestedAppsFlow.Controls.Add(BuildSuggestedAppCard(detected));
            }

            suggestedAppsFlow.ResumeLayout();
            SetSuggestedAppsHeaderState(dismissed: false);
            suggestedAppsSection.Height = 188;
            suggestedAppsSection.Visible = true;
        }

        private void ShowSuggestionsRestoredPrompt()
        {
            // No cards — tell the user dismiss is temporary until AppTime restarts.
            SetSuggestedAppsHeaderState(dismissed: true);
            suggestedAppsFlow.Height = 0;
            suggestedAppsSection.Height = 64;
            suggestedAppsSection.Visible = true;
        }

        private void SetSuggestedAppsHeaderState(bool dismissed)
        {
            if (suggestedAppsSubtitle is not null)
            {
                suggestedAppsSubtitle.Text = dismissed
                    ? "Suggestions were dismissed. Restart the app to restore them."
                    : "Apps detected on your PC that aren't in your library yet.";
            }

            if (suggestedAppsActionLink is not null)
            {
                suggestedAppsActionLink.Text = "Dismiss all";
                suggestedAppsActionLink.Visible = !dismissed;
                if (!dismissed)
                {
                    suggestedAppsActionLink.Location = new Point(
                        Math.Max(0, suggestedAppsSection.ClientSize.Width - suggestedAppsActionLink.Width - 16),
                        16);
                }
            }
        }

        private Control BuildSuggestedAppCard(AppDetector.DetectedApp detected)
        {
            // Compact cards so six fit comfortably in the blue strip.
            const int cardWidth = 158;
            var card = new RoundedPanel
            {
                Width = cardWidth,
                Height = 118,
                Margin = new Padding(2, 0, 6, 0),
                Padding = new Padding(8, 8, 8, 8),
                BackColor = AppTheme.CardBackground
            };

            var icon = new PictureBox
            {
                Image = AppIconCache.GetIcon(detected.ExecutablePath, detected.Name, 28),
                Size = new Size(28, 28),
                Location = new Point(8, 8),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            card.Controls.Add(icon);

            var ignoreButton = new Label
            {
                Text = "×",
                AutoSize = true,
                Location = new Point(cardWidth - 22, 4),
                Font = new Font(AppTheme.Base.FontFamily, 10f),
                ForeColor = AppTheme.TextSecondary,
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };
            ignoreButton.Click += (_, _) => IgnoreSuggestedApplication(detected);
            card.Controls.Add(ignoreButton);

            card.Controls.Add(new Label
            {
                Text = detected.Name,
                AutoSize = false,
                Size = new Size(cardWidth - 16, 28),
                Location = new Point(8, 40),
                Font = AppTheme.CardTitle,
                ForeColor = AppTheme.TextPrimary,
                AutoEllipsis = true
            });

            card.Controls.Add(new Label
            {
                Text = detected.SuggestedCategory,
                AutoSize = true,
                Location = new Point(8, 68),
                Font = AppTheme.SmallText,
                ForeColor = AppTheme.TextSecondary
            });

            var addButton = new Button
            {
                Text = "Add",
                Size = new Size(cardWidth - 16, 26),
                Location = new Point(8, 86),
                FlatStyle = FlatStyle.Flat,
                BackColor = AppTheme.Accent,
                ForeColor = Color.White,
                Font = AppTheme.SmallText,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            addButton.FlatAppearance.BorderSize = 0;
            addButton.Click += (_, _) => AddDetectedApplication(detected);
            ApplyRoundedButtonRegion(addButton, radius: 8);
            card.Controls.Add(addButton);

            return card;
        }

        private static void ApplyRoundedButtonRegion(Button button, int radius)
        {
            if (button.Width <= 0 || button.Height <= 0)
            {
                return;
            }

            var bounds = new Rectangle(0, 0, button.Width, button.Height);
            var diameter = radius * 2;
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            button.Region?.Dispose();
            button.Region = new Region(path);
        }

        private void IgnoreSuggestedApplication(AppDetector.DetectedApp detected)
        {
            if (string.IsNullOrWhiteSpace(detected.ExecutablePath))
            {
                return;
            }

            ignoredSuggestions.Add(detected.ExecutablePath);
            RefreshSuggestedApps();
        }

        private void DismissAllSuggestedApplications()
        {
            try
            {
                var current = AppDetector.DetectSuggestions(allApps, ignoredSuggestions, maxCount: 6);
                foreach (var detected in current)
                {
                    if (!string.IsNullOrWhiteSpace(detected.ExecutablePath))
                    {
                        ignoredSuggestions.Add(detected.ExecutablePath);
                    }
                }
            }
            catch
            {
                // Detection failures shouldn't block dismiss.
            }

            RefreshSuggestedApps();
        }

        private void AddDetectedApplication(AppDetector.DetectedApp detected)
        {
            if (allApps.Any(a =>
                    string.Equals(a.ExecutablePath, detected.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
            {
                RefreshSuggestedApps();
                return;
            }

            using var addForm = new AddApplicationForm(detected.ExecutablePath, detected.SuggestedCategory);
            if (addForm.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            allApps.Add(new AppEntry
            {
                Name = addForm.ApplicationName,
                ExecutablePath = detected.ExecutablePath,
                Category = string.IsNullOrWhiteSpace(addForm.Category)
                    ? detected.SuggestedCategory
                    : addForm.Category
            });

            LibraryStorage.SaveLibrary(allApps);
            ApplyFilter();
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
                    card.StopRequested += (_, _) => StopApplication(app);
                    card.EditRequested += (_, _) => EditApplication(app);
                    card.RemoveRequested += (_, _) => RemoveApplication(app);
                    card.DetailsRequested += (_, _) => ShowAppDetails(app);
                    libraryFlow.Controls.Add(card);
                }
            }

            libraryFlow.ResumeLayout();

            // Freshly created cards default to "not running" until the next timer
            // tick - refresh immediately so switching filters or adding/editing an app
            // doesn't show a stale state for a few seconds.
            RefreshRunningStates();
            RefreshSuggestedApps();
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

            // detailsPanel isn't built yet the first time this runs - Overview (built
            // before detailsPanel in BuildMainContent) populates its Recent/Most Used
            // cards during construction, which calls this. Guard rather than reorder
            // construction, so this stays safe regardless of build order.
            if (detailsPanel is not null && detailsPanel.Visible)
            {
                UpdateDetailsActionButtonAppearance();
            }
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

        // Same process-name matching as IsProcessRunning - see the comment there.
        // Best-effort: a process that's already exited, or one we don't have
        // permission to kill (e.g. elevated), just gets skipped rather than shown as
        // an error, since the end result the user cares about ("it's stopped") is
        // usually still true or about to be true either way.
        private void StopApplication(AppEntry app)
        {
            if (string.IsNullOrWhiteSpace(app.ExecutablePath))
            {
                return;
            }

            var processName = Path.GetFileNameWithoutExtension(app.ExecutablePath);
            if (string.IsNullOrEmpty(processName))
            {
                return;
            }

            var processes = Process.GetProcessesByName(processName);
            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception)
                    {
                        // Already exited, or access denied - nothing more we can do.
                    }
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        private void EditApplication(AppEntry app)
        {
            using var editForm = new EditApplicationForm(app);
            if (editForm.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var previousPath = app.ExecutablePath;
            app.Name = editForm.ApplicationName;
            app.Category = editForm.Category;
            app.ExecutablePath = editForm.ExecutablePath;

            if (!string.Equals(previousPath, app.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                AppIconCache.Invalidate(previousPath);
                AppIconCache.Invalidate(app.ExecutablePath);
            }

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