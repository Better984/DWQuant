using Microsoft.Extensions.DependencyInjection;
using ServerTest.Infrastructure.Db;
using ServerTest.Models;
using ServerTest.Services;
using ServerTest.WebSockets;
using System.Text;
using System.Text.RegularExpressions;

namespace ServerTest.Monitoring
{
    public sealed class StartupMonitorForm : Form
    {
        private const int MaxLogItems = 1000;
        private readonly Func<IReadOnlyList<HistoricalCacheSnapshot>> _historyProvider;
        private readonly Func<IReadOnlyList<WebSocketConnection>> _connectionsProvider;
        private readonly BinanceHistoricalDataDownloader _downloader;
        private readonly IServiceProvider _serviceProvider;

        private ListView _logList = null!;
        private ListView _statusList = null!;
        private CheckedListBox _sourceFilterList = null!;
        private CheckedListBox _levelFilterList = null!;
        private Panel _outputPanel = null!;
        private Panel _historyPanel = null!;
        private Panel _networkPanel = null!;
        private Panel _downloadPanel = null!;
        private TreeView _historyTree = null!;
        private ListView _networkUserList = null!;
        private Label _historySummaryLabel = null!;
        private Label _networkSummaryLabel = null!;
        private Button _toggleOutputButton = null!;
        private Button _toggleHistoryButton = null!;
        private Button _toggleNetworkButton = null!;
        private Button _toggleDownloadButton = null!;
        private Button _toggleTradingButton = null!;
        private Button _toggleUserListButton = null!;
        private bool _networkDetailsExpanded;
        private readonly System.Windows.Forms.Timer _refreshTimer;
        private ComboBox _downloadSymbol = null!;
        private ComboBox _downloadTimeframe = null!;
        private DateTimePicker _downloadStartDate = null!;
        private Button _downloadStartButton = null!;
        private Button _downloadCancelButton = null!;
        private ListBox _downloadLogList = null!;
        private Label _downloadStatusLabel = null!;
        private CheckBox _forceDailyOnlyCheckBox = null!;
        private Button _fillGapsButton = null!;
        private CancellationTokenSource? _downloadCts;
        private bool _downloadRunning;
        private ContextMenuStrip _downloadLogMenu = null!;
        private Panel _tradingPanel = null!;
        private Label _tradingSummaryLabel = null!;
        private ListView _tradingLogList = null!;
        private Button _tradingDetailButton = null!;
        private Button _tradingStatsButton = null!;
        private readonly RealTimeStrategyEngine _strategyEngine;
        private readonly MarketDataEngine _marketDataEngine;
        private readonly List<StartupMonitorLogEntry> _tradingLogEntries = new();
        private const int MaxTradingLogItems = 300;
        private const string StrategyStateLogTag = "[StrategyState]";
        private const string StrategyRunLogTag = "[StrategyRun]";
        private static readonly Regex StrategyStateLogRegex = new(
            @"uid=(\d+)\s+usId=(\d+)\s+state=([a-z_]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex StrategyRunLogRegex = new(
            @"exchange=(\S+)\s+symbol=(\S+)\s+timeframe=(\S+)\s+mode=(\S+)\s+time=(\S+)\s+durationMs=(\d+)\s+matched=(\d+)\s+executed=(\d+)\s+skipped=(\d+)\s+conditions=(\d+)\s+actions=(\d+)\s+openTasks=(\d+)\s+execIds=([^\s]*)\s+openIds=([^\s]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly Dictionary<SystemModule, ListViewItem> _statusItems = new();
        private readonly List<StartupMonitorLogEntry> _logEntries = new();
        private readonly HashSet<string> _selectedSources = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<Microsoft.Extensions.Logging.LogLevel> _selectedLevels = new();
        private bool _suppressSourceFilterEvents;
        private bool _suppressLevelFilterEvents;
        private List<dynamic> _detectedGaps = new();

        public StartupMonitorForm(
            Func<IReadOnlyList<HistoricalCacheSnapshot>> historyProvider,
            Func<IReadOnlyList<WebSocketConnection>> connectionsProvider,
            BinanceHistoricalDataDownloader downloader,
            IServiceProvider serviceProvider)
        {
            _historyProvider = historyProvider ?? (() => Array.Empty<HistoricalCacheSnapshot>());
            _connectionsProvider = connectionsProvider ?? (() => Array.Empty<WebSocketConnection>());
            _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _strategyEngine = serviceProvider.GetRequiredService<RealTimeStrategyEngine>();
            _marketDataEngine = serviceProvider.GetRequiredService<MarketDataEngine>();

            Text = "DWQuant Server Monitor";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(980, 620);
            Width = 1200;
            Height = 760;

            var header = BuildHeader();
            var content = BuildContentPanels();

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(header, 0, 0);
            layout.Controls.Add(content, 0, 1);
            Controls.Add(layout);


            _refreshTimer = new System.Windows.Forms.Timer
            {
                Interval = 2000
            };
            _refreshTimer.Tick += (_, __) =>
            {
                RefreshHistoryData();
                RefreshNetworkData();
                RefreshTradingData();
            };
            _refreshTimer.Start();

            SetActiveView(ViewMode.Output);
        }

        public void LoadStatuses(Dictionary<SystemModule, (SystemStatus Status, string? Error)> statuses)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => LoadStatuses(statuses));
                return;
            }

            _statusList.BeginUpdate();
            _statusList.Items.Clear();
            _statusItems.Clear();

            foreach (var entry in statuses.OrderBy(x => (int)x.Key))
            {
                var item = new ListViewItem(entry.Key.ToString());
                item.SubItems.Add(FormatStatus(entry.Value.Status));
                item.SubItems.Add(entry.Value.Error ?? string.Empty);
                item.Tag = entry.Key;
                ApplyStatusColor(item, entry.Value.Status);
                _statusList.Items.Add(item);
                _statusItems[entry.Key] = item;
            }

            _statusList.EndUpdate();
            UpdateSummaryCounts();
        }

        public void UpdateStatus(SystemModule module, SystemStatus status, string? error)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => UpdateStatus(module, status, error));
                return;
            }

            if (!_statusItems.TryGetValue(module, out var item))
            {
                item = new ListViewItem(module.ToString());
                item.SubItems.Add(FormatStatus(status));
                item.SubItems.Add(error ?? string.Empty);
                item.Tag = module;
                _statusList.Items.Add(item);
                _statusItems[module] = item;
            }
            else
            {
                item.SubItems[1].Text = FormatStatus(status);
                item.SubItems[2].Text = error ?? string.Empty;
            }

            ApplyStatusColor(item, status);
            UpdateSummaryCounts();
        }

        public void AppendLog(StartupMonitorLogEntry entry)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => AppendLog(entry));
                return;
            }

            TrackSource(entry.Category);
            TrackLevel(entry.Level);
            _logEntries.Add(entry);

            if (_logEntries.Count > MaxLogItems)
            {
                _logEntries.RemoveAt(0);
            }

            if (_selectedSources.Contains(entry.Category) && _selectedLevels.Contains(entry.Level))
            {
                AppendLogItem(entry);
                _logList.EnsureVisible(_logList.Items.Count - 1);
            }

            var message = entry.Message ?? string.Empty;
            var isStateLog = message.Contains(StrategyStateLogTag, StringComparison.OrdinalIgnoreCase);
            var isRunLog = message.Contains(StrategyRunLogTag, StringComparison.OrdinalIgnoreCase);
            if (isStateLog || isRunLog)
            {
                _tradingLogEntries.Add(entry);
                if (_tradingLogEntries.Count > MaxTradingLogItems)
                {
                    _tradingLogEntries.RemoveAt(0);
                }

                AppendTradingLog(entry, isStateLog);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            base.OnFormClosed(e);
        }

        private enum ViewMode
        {
            Output,
            History,
            Network,
            Download,
            Trading
        }

        private Control BuildHeader()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 10, 20, 10),
                BackColor = Color.FromArgb(245, 247, 250),
            };

            var title = new Label
            {
                Text = "Startup Monitor",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(0, 0),
            };

            var subtitle = new Label
            {
                Text = "System modules and log stream",
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                AutoSize = true,
                ForeColor = Color.FromArgb(90, 90, 90),
                Location = new Point(2, 32),
            };

            var titlePanel = new Panel
            {
                Dock = DockStyle.Fill,
            };
            titlePanel.Controls.Add(title);
            titlePanel.Controls.Add(subtitle);

            var togglePanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 18, 0, 0),
                WrapContents = false,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            _toggleOutputButton = BuildToggleButton("输出");
            _toggleOutputButton.Click += (_, __) => SetActiveView(ViewMode.Output);
            _toggleHistoryButton = BuildToggleButton("历史行情");
            _toggleHistoryButton.Click += (_, __) => SetActiveView(ViewMode.History);
            _toggleNetworkButton = BuildToggleButton("网络详情");
            _toggleNetworkButton.Click += (_, __) => SetActiveView(ViewMode.Network);
            _toggleDownloadButton = BuildToggleButton("数据下载");
            _toggleDownloadButton.Click += (_, __) => SetActiveView(ViewMode.Download);
            _toggleTradingButton = BuildToggleButton("\u5b9e\u76d8\u8be6\u60c5");
            _toggleTradingButton.Click += (_, __) => SetActiveView(ViewMode.Trading);

            togglePanel.Controls.Add(_toggleOutputButton);
            togglePanel.Controls.Add(_toggleHistoryButton);
            togglePanel.Controls.Add(_toggleNetworkButton);
            togglePanel.Controls.Add(_toggleDownloadButton);
            togglePanel.Controls.Add(_toggleTradingButton);

            var headerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
            };
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 560));

            headerLayout.Controls.Add(titlePanel, 0, 0);
            headerLayout.Controls.Add(togglePanel, 1, 0);

            panel.Controls.Add(headerLayout);
            return panel;
        }

        private static Button BuildToggleButton(string text)
        {
            return new Button
            {
                Text = text,
                Width = 90,
                Height = 30,
                Margin = new Padding(6, 0, 6, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White
            };
        }

        private Control BuildSummaryChip(string text, string name, Color color)
        {
            var chip = new Panel
            {
                Width = 100,
                Height = 48,
                Margin = new Padding(6, 0, 6, 0),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
            };

            var label = new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 8, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(8, 6),
            };

            var value = new Label
            {
                Name = name,
                Text = "0",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                AutoSize = true,
                ForeColor = color,
                Location = new Point(8, 22),
            };

            chip.Controls.Add(label);
            chip.Controls.Add(value);
            return chip;
        }

        private Control BuildContentPanels()
        {
            var container = new Panel
            {
                Dock = DockStyle.Fill
            };

            _outputPanel = BuildOutputPanel();
            _historyPanel = BuildHistoryPanel();
            _networkPanel = BuildNetworkPanel();
            _downloadPanel = BuildDownloadPanel();
            _tradingPanel = BuildTradingPanel();

            container.Controls.Add(_outputPanel);
            container.Controls.Add(_historyPanel);
            container.Controls.Add(_networkPanel);
            container.Controls.Add(_downloadPanel);
            container.Controls.Add(_tradingPanel);

            _outputPanel.Dock = DockStyle.Fill;
            _historyPanel.Dock = DockStyle.Fill;
            _networkPanel.Dock = DockStyle.Fill;
            _downloadPanel.Dock = DockStyle.Fill;
            _tradingPanel.Dock = DockStyle.Fill;
            _outputPanel.Visible = false;
            _historyPanel.Visible = false;
            _networkPanel.Visible = false;
            _downloadPanel.Visible = false;
            _tradingPanel.Visible = false;

            return container;
        }

        private Panel BuildOutputPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill
            };

            var split = BuildSplitPanels();
            panel.Controls.Add(split);
            split.Dock = DockStyle.Fill;
            return panel;
        }

        private SplitContainer BuildSplitPanels()
        {
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 760,
                BackColor = Color.White,
            };

            split.Panel1.Controls.Add(BuildLogPanel());
            split.Panel2.Controls.Add(BuildStatusPanel());

            return split;
        }

        private Panel BuildTradingPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                BackColor = Color.FromArgb(245, 247, 250),
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 520,
                BackColor = Color.White
            };

            var summaryPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                BackColor = Color.White
            };

            var title = new Label
            {
                Text = "\u5b9e\u76d8\u8be6\u60c5",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(4, 4)
            };

            var summaryLabel = new Label
            {
                Text = "\u8fd0\u884c\u4e2d\u7b56\u7565: 0",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                AutoSize = true,
                ForeColor = Color.FromArgb(34, 197, 94),
                Location = new Point(6, 36)
            };
            _tradingSummaryLabel = summaryLabel;

            summaryPanel.Controls.Add(title);
            summaryPanel.Controls.Add(summaryLabel);

            var logPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                BackColor = Color.White
            };

            var logTitle = new Label
            {
                Text = "\u7b56\u7565\u72b6\u6001\u4fee\u6539\u65e5\u5fd7",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(4, 4)
            };

            _tradingLogList = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Dock = DockStyle.Bottom,
                Height = 400
            };
            _tradingLogList.Columns.Add("\u65f6\u95f4", 90);
            _tradingLogList.Columns.Add("\u7528\u6237ID", 100);
            _tradingLogList.Columns.Add("\u7b56\u7565ID", 100);
            _tradingLogList.Columns.Add("\u72b6\u6001", 120);
            _tradingLogList.Columns.Add("\u5185\u5bb9", 360);

            logPanel.Controls.Add(logTitle);
            logPanel.Controls.Add(_tradingLogList);
            _tradingLogList.Dock = DockStyle.Fill;

            split.Panel1.Controls.Add(summaryPanel);
            split.Panel2.Controls.Add(logPanel);

            var footer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(10, 8, 10, 8)
            };

            var buttonRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
            };

            _tradingDetailButton = new Button
            {
                Text = "\u5b9e\u76d8\u8be6\u60c5",
                Width = 100,
                Height = 30,
                BackColor = Color.FromArgb(17, 24, 39),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };

            _tradingStatsButton = new Button
            {
                Text = "\u5f00\u4ed3\u7edf\u8ba1",
                Width = 100,
                Height = 30,
                Enabled = false,
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };

            buttonRow.Controls.Add(_tradingDetailButton);
            buttonRow.Controls.Add(_tradingStatsButton);
            footer.Controls.Add(buttonRow);

            layout.Controls.Add(split, 0, 0);
            layout.Controls.Add(footer, 0, 1);

            panel.Controls.Add(layout);
            return panel;
        }

        private Panel BuildHistoryPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16)
            };

            var title = new Label
            {
                Text = "History Cache",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(6, 4)
            };

            _historySummaryLabel = new Label
            {
                Text = "Symbols: 0 | Rows: 0",
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                AutoSize = true,
                ForeColor = Color.FromArgb(90, 90, 90),
                Location = new Point(8, 30)
            };

            _historyTree = new TreeView
            {
                Dock = DockStyle.Bottom,
                HideSelection = false,
                FullRowSelect = true,
                ShowLines = true,
                ShowNodeToolTips = false,
                Height = 400
            };

            panel.Controls.Add(title);
            panel.Controls.Add(_historySummaryLabel);
            panel.Controls.Add(_historyTree);
            _historyTree.Dock = DockStyle.Fill;
            return panel;
        }

private Panel BuildNetworkPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16)
            };

            var title = new Label
            {
                Text = "网络详情",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(6, 4)
            };

            _networkSummaryLabel = new Label
            {
                Text = "WebSocket 连接: 0",
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                AutoSize = true,
                ForeColor = Color.FromArgb(90, 90, 90),
                Location = new Point(8, 30)
            };

            _toggleUserListButton = new Button
            {
                Text = "展开用户列表",
                Width = 120,
                Height = 26,
                Location = new Point(8, 54)
            };
            _toggleUserListButton.Click += (_, __) =>
            {
                _networkDetailsExpanded = !_networkDetailsExpanded;
                _networkUserList.Visible = _networkDetailsExpanded;
                _toggleUserListButton.Text = _networkDetailsExpanded ? "收起用户列表" : "展开用户列表";
            };

            _networkUserList = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Dock = DockStyle.Bottom,
                Height = 400,
                Visible = false
            };
            _networkUserList.Columns.Add("用户ID", 260);
            _networkUserList.Columns.Add("连接数", 80);

            panel.Controls.Add(title);
            panel.Controls.Add(_networkSummaryLabel);
            panel.Controls.Add(_toggleUserListButton);
            panel.Controls.Add(_networkUserList);
            _networkUserList.Dock = DockStyle.Fill;
            return panel;
        }

        private Panel BuildDownloadPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16)
            };

            var title = new Label
            {
                Text = "数据下载",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(6, 4)
            };

            var info = new Label
            {
                Text = "Binance Futures 下载日线 ZIP（检测首日后补齐至今天）",
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                AutoSize = true,
                ForeColor = Color.FromArgb(90, 90, 90),
                Location = new Point(8, 30)
            };

            var formPanel = new TableLayoutPanel
            {
                Location = new Point(8, 60),
                AutoSize = true,
                ColumnCount = 6,
                RowCount = 4
            };
            formPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            formPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            formPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            formPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            formPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            formPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));

            var symbolLabel = new Label
            {
                Text = "币种",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _downloadSymbol = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 130
            };
            _downloadSymbol.Items.AddRange(Enum.GetValues(typeof(MarketDataConfig.SymbolEnum))
                .Cast<object>()
                .ToArray());
            _downloadSymbol.SelectedIndex = 0;

            var tfLabel = new Label
            {
                Text = "周期",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _downloadTimeframe = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 120
            };
            _downloadTimeframe.Items.AddRange(Enum.GetValues(typeof(MarketDataConfig.TimeframeEnum))
                .Cast<object>()
                .ToArray());
            _downloadTimeframe.SelectedIndex = 0;

            var startLabel = new Label
            {
                Text = "起始日期",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _downloadStartDate = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy-MM-dd",
                Value = new DateTime(2020, 1, 1)
            };

            _downloadStartButton = new Button
            {
                Text = "开始下载",
                Width = 100,
                Height = 28
            };
            _downloadStartButton.Click += async (_, __) => await StartDownloadAsync();

            _downloadCancelButton = new Button
            {
                Text = "停止",
                Width = 60,
                Height = 28,
                Enabled = false
            };
            _downloadCancelButton.Click += (_, __) => _downloadCts?.Cancel();

            _forceDailyOnlyCheckBox = new CheckBox
            {
                Text = "强制日线下载",
                AutoSize = true,
                Checked = false
            };

            var sortButton = new Button
            {
                Text = "按时间排序",
                Width = 100,
                Height = 28
            };
            sortButton.Click += async (_, __) => await SortDataByTimeAsync();

            var checkBaseIntegrityButton = new Button
            {
                Text = "验证基础数据完整性",
                Width = 140,
                Height = 28
            };
            checkBaseIntegrityButton.Click += async (_, __) => await CheckBaseDataIntegrityAsync();

            var checkAllTimeframesButton = new Button
            {
                Text = "验证所有周期完整性",
                Width = 140,
                Height = 28
            };
            checkAllTimeframesButton.Click += async (_, __) => await CheckAllTimeframesIntegrityAsync();

            var aggregateButton = new Button
            {
                Text = "同步聚合其他周期",
                Width = 140,
                Height = 28
            };
            aggregateButton.Click += async (_, __) => await AggregateTimeframesAsync();

            _fillGapsButton = new Button
            {
                Text = "尝试补齐数据",
                Width = 120,
                Height = 28,
                Enabled = false
            };
            _fillGapsButton.Click += async (_, __) => await TryFillGapsAsync();

            formPanel.Controls.Add(symbolLabel, 0, 0);
            formPanel.Controls.Add(_downloadSymbol, 1, 0);
            formPanel.Controls.Add(tfLabel, 2, 0);
            formPanel.Controls.Add(_downloadTimeframe, 3, 0);
            formPanel.Controls.Add(startLabel, 4, 0);
            formPanel.Controls.Add(_downloadStartDate, 5, 0);
            formPanel.Controls.Add(_downloadStartButton, 1, 1);
            formPanel.Controls.Add(_downloadCancelButton, 2, 1);
            formPanel.Controls.Add(_forceDailyOnlyCheckBox, 3, 1);
            formPanel.Controls.Add(sortButton, 0, 2);
            formPanel.Controls.Add(checkBaseIntegrityButton, 1, 2);
            formPanel.Controls.Add(checkAllTimeframesButton, 2, 2);
            formPanel.Controls.Add(aggregateButton, 0, 3);
            formPanel.Controls.Add(_fillGapsButton, 1, 3);

            _downloadStatusLabel = new Label
            {
                Text = "状态：待机",
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                AutoSize = true,
                ForeColor = Color.FromArgb(90, 90, 90),
                Location = new Point(8, 130)
            };

            _downloadLogList = new ListBox
            {
                Location = new Point(8, 160),
                Width = 920,
                Height = 420,
                HorizontalScrollbar = true,
                SelectionMode = SelectionMode.MultiExtended
            };
            _downloadLogList.KeyDown += (_, e) =>
            {
                if (e.Control && e.KeyCode == Keys.C)
                {
                    CopySelectedDownloadLogsToClipboard();
                    e.Handled = true;
                }
            };

            _downloadLogMenu = new ContextMenuStrip();
            var copyMenuItem = new ToolStripMenuItem("Copy");
            copyMenuItem.Click += (_, __) => CopySelectedDownloadLogsToClipboard();
            _downloadLogMenu.Items.Add(copyMenuItem);
            _downloadLogList.ContextMenuStrip = _downloadLogMenu;

            panel.Controls.Add(title);
            panel.Controls.Add(info);
            panel.Controls.Add(formPanel);
            panel.Controls.Add(_downloadStatusLabel);
            panel.Controls.Add(_downloadLogList);
            return panel;
        }

        private Control BuildLogPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
            };

            var title = new Label
            {
                Text = "Log Stream",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                AutoSize = true,
                Dock = DockStyle.Top,
            };

            var contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(6, 34, 6, 6),
            };

            var filterPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 200,
            };

            // 使用 SplitContainer 分隔 Level 和 Source 筛选
            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 200,
                SplitterWidth = 4,
                FixedPanel = FixedPanel.None
            };

            // Level 筛选区域（上方）
            var levelPanel = new Panel
            {
                Dock = DockStyle.Fill,
            };

            var levelFilterLabel = new Label
            {
                Text = "Level",
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                Dock = DockStyle.Top,
                Height = 22,
            };

            var levelFilterActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 2, 0, 4),
                WrapContents = false,
            };

            var selectAllLevelButton = new Button
            {
                Text = "All",
                Width = 70,
                Height = 24,
            };
            selectAllLevelButton.Click += (_, __) => SetLevelFilterSelection(true);

            var selectNoneLevelButton = new Button
            {
                Text = "None",
                Width = 70,
                Height = 24,
            };
            selectNoneLevelButton.Click += (_, __) => SetLevelFilterSelection(false);

            levelFilterActions.Controls.Add(selectAllLevelButton);
            levelFilterActions.Controls.Add(selectNoneLevelButton);

            var levelFilterList = new CheckedListBox
            {
                Name = "LevelFilterList",
                CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false,
                Dock = DockStyle.Fill,
            };
            levelFilterList.ItemCheck += (_, __) =>
            {
                if (_suppressLevelFilterEvents)
                {
                    return;
                }

                if (IsHandleCreated)
                {
                    BeginInvoke(new Action(ApplyFilters));
                }
                else
                {
                    ApplyFilters();
                }
            };

            // 初始化 Level 列表
            var allLevels = Enum.GetValues<Microsoft.Extensions.Logging.LogLevel>()
                .OrderBy(l => l)
                .ToList();
            _suppressLevelFilterEvents = true;
            foreach (var level in allLevels)
            {
                levelFilterList.Items.Add(level, true);
            }
            _suppressLevelFilterEvents = false;
            _selectedLevels.UnionWith(allLevels);

            levelPanel.Controls.Add(levelFilterList);
            levelPanel.Controls.Add(levelFilterActions);
            levelPanel.Controls.Add(levelFilterLabel);

            // Source 筛选区域（下方）
            var sourcePanel = new Panel
            {
                Dock = DockStyle.Fill,
            };

            var sourceFilterLabel = new Label
            {
                Text = "Sources",
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                Dock = DockStyle.Top,
                Height = 22,
            };

            var sourceFilterActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 2, 0, 4),
                WrapContents = false,
            };

            var selectAllSourceButton = new Button
            {
                Text = "All",
                Width = 70,
                Height = 24,
            };
            selectAllSourceButton.Click += (_, __) => SetSourceFilterSelection(true);

            var selectNoneSourceButton = new Button
            {
                Text = "None",
                Width = 70,
                Height = 24,
            };
            selectNoneSourceButton.Click += (_, __) => SetSourceFilterSelection(false);

            sourceFilterActions.Controls.Add(selectAllSourceButton);
            sourceFilterActions.Controls.Add(selectNoneSourceButton);

            var sourceFilterList = new CheckedListBox
            {
                Name = "SourceFilterList",
                CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false,
                Dock = DockStyle.Fill,
            };
            sourceFilterList.ItemCheck += (_, __) =>
            {
                if (_suppressSourceFilterEvents)
                {
                    return;
                }

                if (IsHandleCreated)
                {
                    BeginInvoke(new Action(ApplyFilters));
                }
                else
                {
                    ApplyFilters();
                }
            };

            sourcePanel.Controls.Add(sourceFilterList);
            sourcePanel.Controls.Add(sourceFilterActions);
            sourcePanel.Controls.Add(sourceFilterLabel);

            splitContainer.Panel1.Controls.Add(levelPanel);
            splitContainer.Panel2.Controls.Add(sourcePanel);

            filterPanel.Controls.Add(splitContainer);

            _sourceFilterList = sourceFilterList;
            _levelFilterList = levelFilterList;

            var list = new ListView
            {
                Name = "LogList",
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Dock = DockStyle.Bottom,
                Height = 500,
            };
            list.Columns.Add("Time", 80);
            list.Columns.Add("Level", 80);
            list.Columns.Add("Source", 180);
            list.Columns.Add("Message", 460);
            list.KeyDown += (_, e) =>
            {
                if (e.Control && e.KeyCode == Keys.C)
                {
                    CopySelectedLogsToClipboard();
                    e.Handled = true;
                }
            };

            var logMenu = new ContextMenuStrip();
            var copyMenuItem = new ToolStripMenuItem("Copy");
            copyMenuItem.Click += (_, __) => CopySelectedLogsToClipboard();
            logMenu.Items.Add(copyMenuItem);
            list.ContextMenuStrip = logMenu;

            _logList = list;

            panel.Controls.Add(title);
            panel.Controls.Add(contentPanel);
            contentPanel.Controls.Add(list);
            contentPanel.Controls.Add(filterPanel);
            list.Dock = DockStyle.Fill;
            return panel;
        }

        private Control BuildStatusPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
            };

            var title = new Label
            {
                Text = "System Status",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(6, 4),
            };

            var list = new ListView
            {
                Name = "StatusList",
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Dock = DockStyle.Bottom,
                Height = 400,
            };
            list.Columns.Add("Module", 160);
            list.Columns.Add("Status", 120);
            list.Columns.Add("Message", 260);

            _statusList = list;

            panel.Controls.Add(title);
            panel.Controls.Add(list);
            list.Dock = DockStyle.Fill;
            return panel;
        }

        private static string FormatStatus(SystemStatus status)
        {
            return status switch
            {
                SystemStatus.Ready => "就绪",
                SystemStatus.Starting => "启动中",
                SystemStatus.Failed => "失败",
                _ => "未启动"
            };
        }

        private static void ApplyStatusColor(ListViewItem item, SystemStatus status)
        {
            item.ForeColor = status switch
            {
                SystemStatus.Ready => Color.FromArgb(30, 150, 85),
                SystemStatus.Starting => Color.FromArgb(210, 140, 40),
                SystemStatus.Failed => Color.FromArgb(210, 70, 70),
                _ => Color.FromArgb(90, 90, 90)
            };
        }

        private static Color GetLevelColor(Microsoft.Extensions.Logging.LogLevel level)
        {
            return level switch
            {
                Microsoft.Extensions.Logging.LogLevel.Warning => Color.FromArgb(200, 140, 40),
                Microsoft.Extensions.Logging.LogLevel.Error => Color.FromArgb(210, 70, 70),
                Microsoft.Extensions.Logging.LogLevel.Critical => Color.FromArgb(200, 0, 0),
                _ => Color.FromArgb(40, 40, 40)
            };
        }

        private void TrackSource(string category)
        {
            if (_sourceFilterList.Items.Contains(category))
            {
                return;
            }

            _suppressSourceFilterEvents = true;
            _sourceFilterList.Items.Add(category, true);
            _suppressSourceFilterEvents = false;
            _selectedSources.Add(category);
        }

        private void TrackLevel(Microsoft.Extensions.Logging.LogLevel level)
        {
            // Level 已在初始化时添加，这里无需额外处理
        }

        private void SetSourceFilterSelection(bool isChecked)
        {
            if (_sourceFilterList.Items.Count == 0)
            {
                return;
            }

            _suppressSourceFilterEvents = true;
            for (var i = 0; i < _sourceFilterList.Items.Count; i++)
            {
                _sourceFilterList.SetItemChecked(i, isChecked);
            }
            _suppressSourceFilterEvents = false;

            ApplySourceFilter();
        }

        private void SetLevelFilterSelection(bool isChecked)
        {
            if (_levelFilterList.Items.Count == 0)
            {
                return;
            }

            _suppressLevelFilterEvents = true;
            for (var i = 0; i < _levelFilterList.Items.Count; i++)
            {
                _levelFilterList.SetItemChecked(i, isChecked);
            }
            _suppressLevelFilterEvents = false;

            ApplyFilters();
        }

        private void ApplyFilters()
        {
            // 更新选中的 Sources
            _selectedSources.Clear();
            foreach (var item in _sourceFilterList.CheckedItems)
            {
                if (item is string source)
                {
                    _selectedSources.Add(source);
                }
            }

            // 更新选中的 Levels
            _selectedLevels.Clear();
            foreach (var item in _levelFilterList.CheckedItems)
            {
                if (item is Microsoft.Extensions.Logging.LogLevel level)
                {
                    _selectedLevels.Add(level);
                }
            }

            // 应用筛选
            _logList.BeginUpdate();
            _logList.Items.Clear();
            foreach (var entry in _logEntries)
            {
                if (_selectedSources.Contains(entry.Category) && _selectedLevels.Contains(entry.Level))
                {
                    AppendLogItem(entry);
                }
            }
            _logList.EndUpdate();
        }

        private void ApplySourceFilter()
        {
            ApplyFilters();
        }

        private void CopySelectedLogsToClipboard()
        {
            if (_logList.SelectedItems.Count == 0)
            {
                return;
            }

            var lines = _logList.SelectedItems
                .Cast<ListViewItem>()
                .OrderBy(item => item.Index)
                .Select(item => string.Join("\t", item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(sub => sub.Text)))
                .ToList();

            var builder = new StringBuilder();
            foreach (var line in lines)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }
                builder.Append(line);
            }

            Clipboard.SetText(builder.ToString());
        }

        private void AppendLogItem(StartupMonitorLogEntry entry)
        {
            var item = new ListViewItem(entry.Timestamp.ToString("HH:mm:ss"));
            item.SubItems.Add(entry.Level.ToString().ToUpperInvariant());
            item.SubItems.Add(entry.Category);
            item.SubItems.Add(entry.Message);
            item.ForeColor = GetLevelColor(entry.Level);
            _logList.Items.Add(item);
        }

        private void SetActiveView(ViewMode mode)
        {
            _outputPanel.Visible = mode == ViewMode.Output;
            _historyPanel.Visible = mode == ViewMode.History;
            _networkPanel.Visible = mode == ViewMode.Network;
            _downloadPanel.Visible = mode == ViewMode.Download;
            _tradingPanel.Visible = mode == ViewMode.Trading;

            if (mode == ViewMode.Output)
            {
                _outputPanel.BringToFront();
            }
            else if (mode == ViewMode.History)
            {
                _historyPanel.BringToFront();
            }
            else if (mode == ViewMode.Network)
            {
                _networkPanel.BringToFront();
            }
            else if (mode == ViewMode.Trading)
            {
                _tradingPanel.BringToFront();
            }
            else
            {
                _downloadPanel.BringToFront();
            }

            ApplyToggleStyle(_toggleOutputButton, mode == ViewMode.Output);
            ApplyToggleStyle(_toggleHistoryButton, mode == ViewMode.History);
            ApplyToggleStyle(_toggleNetworkButton, mode == ViewMode.Network);
            ApplyToggleStyle(_toggleDownloadButton, mode == ViewMode.Download);
            ApplyToggleStyle(_toggleTradingButton, mode == ViewMode.Trading);

            if (mode == ViewMode.History)
            {
                RefreshHistoryData();
            }
            else if (mode == ViewMode.Network)
            {
                RefreshNetworkData();
            }
            else if (mode == ViewMode.Trading)
            {
                RefreshTradingData();
            }
        }

        private static void ApplyToggleStyle(Button button, bool isActive)
        {
            button.BackColor = isActive ? Color.FromArgb(35, 110, 190) : Color.White;
            button.ForeColor = isActive ? Color.White : Color.Black;
        }

        private void RefreshHistoryData()
        {
            if (!_historyPanel.Visible)
            {
                return;
            }

            IReadOnlyList<HistoricalCacheSnapshot> data;
            try
            {
                data = _historyProvider.Invoke();
            }
            catch
            {
                return;
            }

            var groups = data
                .GroupBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectExpandedNodes(_historyTree.Nodes, expanded);
            var selectedKey = _historyTree.SelectedNode?.FullPath;
            var topKey = _historyTree.TopNode?.FullPath;

            _historyTree.BeginUpdate();
            _historyTree.Nodes.Clear();

            foreach (var group in groups)
            {
                var exchanges = group.Select(item => item.Exchange)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var groupLabel = exchanges.Count <= 1
                    ? group.Key
                    : $"{group.Key} [{string.Join(", ", exchanges)}]";

                var symbolNode = new TreeNode(groupLabel);
                foreach (var item in group.OrderBy(i => i.Timeframe, StringComparer.OrdinalIgnoreCase))
                {
                    var line = $"{item.Timeframe} | {item.Count} | {item.StartTime:yyyy-MM-dd HH:mm:ss} -> {item.EndTime:yyyy-MM-dd HH:mm:ss}";
                    symbolNode.Nodes.Add(new TreeNode(line));
                }

                _historyTree.Nodes.Add(symbolNode);
            }

            RestoreExpandedNodes(_historyTree.Nodes, expanded);
            if (!string.IsNullOrWhiteSpace(selectedKey))
            {
                var selectedNode = FindNodeByPath(_historyTree.Nodes, selectedKey);
                if (selectedNode != null)
                {
                    _historyTree.SelectedNode = selectedNode;
                }
            }

            if (!string.IsNullOrWhiteSpace(topKey))
            {
                var topNode = FindNodeByPath(_historyTree.Nodes, topKey);
                if (topNode != null)
                {
                    _historyTree.TopNode = topNode;
                }
            }

            _historyTree.EndUpdate();
            _historySummaryLabel.Text = $"Symbols: {groups.Count} | Rows: {data.Count}";
        }

        private static void CollectExpandedNodes(TreeNodeCollection nodes, HashSet<string> expanded)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.IsExpanded)
                {
                    expanded.Add(node.FullPath);
                }

                if (node.Nodes.Count > 0)
                {
                    CollectExpandedNodes(node.Nodes, expanded);
                }
            }
        }

        private static void RestoreExpandedNodes(TreeNodeCollection nodes, HashSet<string> expanded)
        {
            foreach (TreeNode node in nodes)
            {
                if (expanded.Contains(node.FullPath))
                {
                    node.Expand();
                }

                if (node.Nodes.Count > 0)
                {
                    RestoreExpandedNodes(node.Nodes, expanded);
                }
            }
        }

        private static TreeNode? FindNodeByPath(TreeNodeCollection nodes, string fullPath)
        {
            foreach (TreeNode node in nodes)
            {
                if (string.Equals(node.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return node;
                }

                if (node.Nodes.Count > 0)
                {
                    var match = FindNodeByPath(node.Nodes, fullPath);
                    if (match != null)
                    {
                        return match;
                    }
                }
            }

            return null;
        }

        private void RefreshNetworkData()
        {
            if (!_networkPanel.Visible)
            {
                return;
            }

            IReadOnlyList<WebSocketConnection> connections;
            try
            {
                connections = _connectionsProvider.Invoke();
            }
            catch
            {
                return;
            }

            var userGroups = connections
                .GroupBy(c => c.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ThenBy(g => g.UserId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _networkSummaryLabel.Text = $"WebSocket 连接: {connections.Count} | 用户数 {userGroups.Count}";

            _networkUserList.BeginUpdate();
            _networkUserList.Items.Clear();
            foreach (var user in userGroups)
            {
                var row = new ListViewItem(user.UserId);
                row.SubItems.Add(user.Count.ToString());
                _networkUserList.Items.Add(row);
            }
            _networkUserList.EndUpdate();
        }

        private void RefreshTradingData()
        {
            if (_tradingSummaryLabel == null)
            {
                return;
            }

            _strategyEngine.GetStateCounts(out var running, out var pausedOpen, out var testing, out var total);

            var tickInfo = _marketDataEngine.GetNextTickInfo();
            var nextSeconds = tickInfo.Next1mCloseInSeconds.HasValue ? $"{tickInfo.Next1mCloseInSeconds.Value}s" : "-";
            var updateTfText = tickInfo.UpdateTimeframes.Count == 0 ? "-" : string.Join(",", tickInfo.UpdateTimeframes);
            var closeTfText = tickInfo.ClosingTimeframes.Count == 0 ? "-" : string.Join(",", tickInfo.ClosingTimeframes);

            var timeframeSecs = new HashSet<int>();
            timeframeSecs.Add(60);
            foreach (var tf in tickInfo.UpdateTimeframes)
            {
                var ms = MarketDataConfig.TimeframeToMs(tf);
                if (ms > 0)
                {
                    timeframeSecs.Add((int)(ms / 1000));
                }
            }

            var nextCheckCount = _strategyEngine.GetRunnableStrategyCountForTimeframes(timeframeSecs);

            _tradingSummaryLabel.Text =
                $"运行中: {running} | 暂停开新仓: {pausedOpen} | 测试: {testing} | 总数: {total}\n" +
                $"下一次1m收线: {nextSeconds} | OnBarUpdate周期: {updateTfText}\n" +
                $"即将收线周期: {closeTfText} | 下次检查策略数: {nextCheckCount}";
        }

        private void AppendTradingLog(StartupMonitorLogEntry entry, bool isStateLog)
        {
            if (_tradingLogList == null)
            {
                return;
            }

            var message = entry.Message ?? string.Empty;
            var uidText = "-";
            var usIdText = "-";
            var stateText = "-";
            var contentText = message
                .Replace(StrategyStateLogTag, string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace(StrategyRunLogTag, string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();

            if (isStateLog)
            {
                var match = StrategyStateLogRegex.Match(message);
                if (match.Success)
                {
                    uidText = match.Groups[1].Value;
                    usIdText = match.Groups[2].Value;
                    stateText = ToChineseState(match.Groups[3].Value);
                    contentText = $"用户={uidText} 策略实例={usIdText} 状态={stateText}";
                }
            }
            else
            {
                var match = StrategyRunLogRegex.Match(message);
                if (!match.Success)
                {
                    return;
                }

                var executedCount = TryParseInt(match.Groups[8].Value);
                if (executedCount <= 0)
                {
                    return;
                }

                var modeText = ToChineseMode(match.Groups[4].Value);
                var timeText = match.Groups[5].Value.Replace('T', ' ');
                var execIds = match.Groups[13].Value;
                var openIds = match.Groups[14].Value;

                stateText = modeText;
                contentText =
                    $"交易所={match.Groups[1].Value} 币对={match.Groups[2].Value} 周期={match.Groups[3].Value} " +
                    $"模式={modeText} 时间={timeText} 耗时={match.Groups[6].Value}ms " +
                    $"命中={match.Groups[7].Value} 执行={match.Groups[8].Value} 跳过={match.Groups[9].Value} " +
                    $"条件={match.Groups[10].Value} 动作={match.Groups[11].Value} 开仓={match.Groups[12].Value} " +
                    $"执行策略={FormatIdList(execIds)} 开仓策略={FormatIdList(openIds)}";
            }

            var item = new ListViewItem(entry.Timestamp.ToString("HH:mm:ss"));
            item.SubItems.Add(uidText);
            item.SubItems.Add(usIdText);
            item.SubItems.Add(stateText);
            item.SubItems.Add(contentText);

            _tradingLogList.Items.Add(item);
            if (_tradingLogList.Items.Count > MaxTradingLogItems)
            {
                _tradingLogList.Items.RemoveAt(0);
            }

            _tradingLogList.EnsureVisible(_tradingLogList.Items.Count - 1);
        }

        private static string ToChineseState(string state)
        {
            return state?.Trim().ToLowerInvariant() switch
            {
                "running" => "运行中",
                "paused" => "已暂停",
                "paused_open_position" => "暂停开新仓",
                "completed" => "完成",
                "testing" => "测试",
                "error" => "错误",
                _ => state ?? "-"
            };
        }

        private static string ToChineseMode(string mode)
        {
            return mode?.Trim().ToLowerInvariant() switch
            {
                "close" => "收线",
                "update" => "更新",
                _ => mode ?? "-"
            };
        }

        private static int TryParseInt(string value)
        {
            return int.TryParse(value, out var result) ? result : 0;
        }

        private static string FormatIdList(string ids)
        {
            return string.IsNullOrWhiteSpace(ids) ? "-" : ids;
        }

        private async Task StartDownloadAsync()
        {
            if (_downloadRunning)
            {
                return;
            }

            if (_downloadSymbol.SelectedItem is not MarketDataConfig.SymbolEnum symbolEnum)
            {
                return;
            }

            if (_downloadTimeframe.SelectedItem is not MarketDataConfig.TimeframeEnum timeframeEnum)
            {
                return;
            }

            _downloadRunning = true;
            _downloadStartButton.Enabled = false;
            _downloadCancelButton.Enabled = true;
            _downloadLogList.Items.Clear();
            _downloadStatusLabel.Text = "状态：进行中...";

            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            _downloadCts = new CancellationTokenSource();

            var startDate = _downloadStartDate.Value.Date;
            AppendDownloadLog(new DownloadLogEntry
            {
                Level = DownloadLogLevel.Info,
                Message = $"Start: {symbolEnum} {timeframeEnum} from {startDate:yyyy-MM-dd}"
            });

            try
            {
                var forceDailyOnly = _forceDailyOnlyCheckBox.Checked;
                var summary = await Task.Run(
                    () => _downloader.DownloadAsync(symbolEnum, timeframeEnum, startDate, AppendDownloadLog, _downloadCts.Token, forceDailyOnly),
                    _downloadCts.Token);

                _downloadStatusLabel.Text = $"状态：完成, 澶╂暟={summary.DaysProcessed}, 涓嬭浇={summary.DaysDownloaded}, 鍏ュ簱={summary.RowsInserted}";
            }
            catch (OperationCanceledException)
            {
                _downloadStatusLabel.Text = "状态：已停止";
            }
            catch (Exception ex)
            {
                _downloadStatusLabel.Text = "状态：失败";
                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Error,
                    Message = $"Error: {ex.Message}"
                });
            }
            finally
            {
                _downloadRunning = false;
                _downloadStartButton.Enabled = true;
                _downloadCancelButton.Enabled = false;
            }
        }

        private void AppendDownloadLog(DownloadLogEntry entry)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => AppendDownloadLog(entry));
                return;
            }

            var prefix = entry.Level switch
            {
                DownloadLogLevel.Warning => "[WARN]",
                DownloadLogLevel.Error => "[ERROR]",
                _ => "[INFO]"
            };

            var line = $"{entry.Timestamp:HH:mm:ss} {prefix} {entry.Message}";
            _downloadLogList.Items.Add(line);

            if (_downloadLogList.Items.Count > 500)
            {
                _downloadLogList.Items.RemoveAt(0);
            }

            _downloadLogList.TopIndex = Math.Max(0, _downloadLogList.Items.Count - 1);
        }

        private void CopySelectedDownloadLogsToClipboard()
        {
            if (_downloadLogList.SelectedItems.Count == 0)
            {
                return;
            }

            var lines = _downloadLogList.SelectedItems
                .Cast<object>()
                .Select(item => item?.ToString() ?? string.Empty)
                .ToList();

            Clipboard.SetText(string.Join(Environment.NewLine, lines));
        }

        private void UpdateSummaryCounts()
        {
            // 统计功能已移除
        }

        private async Task SortDataByTimeAsync()
        {
            if (_downloadSymbol.SelectedItem is not MarketDataConfig.SymbolEnum symbolEnum ||
                _downloadTimeframe.SelectedItem is not MarketDataConfig.TimeframeEnum timeframeEnum)
            {
                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Warning,
                    Message = "请先选择币种和周期"
                });
                return;
            }

            try
            {
                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Info,
                    Message = "开始按时间排序..."
                });

                var dbManager = _serviceProvider.GetRequiredService<IDbManager>();
                var exchangeId = MarketDataConfig.ExchangeToString(MarketDataConfig.ExchangeEnum.Binance);
                var symbolStr = MarketDataConfig.SymbolToString(symbolEnum);
                var timeframeStr = MarketDataConfig.TimeframeToString(timeframeEnum);
                var tableName = BuildTableName(exchangeId, symbolStr, timeframeStr);

                // 检查表是否存在
                var checkTableSql = @"SELECT 1 FROM information_schema.tables
WHERE table_schema = DATABASE() AND table_name = @Table
LIMIT 1;";
                var exists = await dbManager.QuerySingleOrDefaultAsync<int?>(checkTableSql, new { Table = tableName }, null, default);
                if (!exists.HasValue)
                {
                    AppendDownloadLog(new DownloadLogEntry
                    {
                        Level = DownloadLogLevel.Warning,
                        Message = $"表 {tableName} 不存在"
                    });
                    return;
                }

                // 创建临时表并排序
                var tempTableName = $"{tableName}_temp_{DateTime.UtcNow:yyyyMMddHHmmss}";
                var createTempSql = $@"CREATE TABLE `{tempTableName}` LIKE `{tableName}`;";
                await dbManager.ExecuteAsync(createTempSql, null, null, default);

                var insertSortedSql = $@"INSERT INTO `{tempTableName}` 
SELECT * FROM `{tableName}` ORDER BY open_time ASC;";
                var rowsAffected = await dbManager.ExecuteAsync(insertSortedSql, null, null, default);

                // 删除原表并重命名
                var dropOriginalSql = $@"DROP TABLE `{tableName}`;";
                await dbManager.ExecuteAsync(dropOriginalSql, null, null, default);

                var renameSql = $@"RENAME TABLE `{tempTableName}` TO `{tableName}`;";
                await dbManager.ExecuteAsync(renameSql, null, null, default);

                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Info,
                    Message = $"排序完成，共处理 {rowsAffected} 条记录"
                });
            }
            catch (Exception ex)
            {
                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Error,
                    Message = $"排序失败: {ex.Message}"
                });
            }
        }

        private async Task CheckBaseDataIntegrityAsync()
        {
            if (_downloadSymbol.SelectedItem is not MarketDataConfig.SymbolEnum symbolEnum)
            {
                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Warning,
                    Message = "请先选择币种"
                });
                return;
            }

            try
            {
                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Info,
                    Message = "开始检查基础数据完整性（1分钟周期）..."
                });

                var dbManager = _serviceProvider.GetRequiredService<IDbManager>();
                var exchangeId = MarketDataConfig.ExchangeToString(MarketDataConfig.ExchangeEnum.Binance);
                var symbolStr = MarketDataConfig.SymbolToString(symbolEnum);
                var timeframeStr = "1m"; // 固定 1 分钟
                var tableName = BuildTableName(exchangeId, symbolStr, timeframeStr);
                var timeframeMs = MarketDataConfig.TimeframeToMs(timeframeStr);

                // 检查表是否存在
                var checkTableSql = @"SELECT 1 FROM information_schema.tables
WHERE table_schema = DATABASE() AND table_name = @Table
LIMIT 1;";
                var exists = await dbManager.QuerySingleOrDefaultAsync<int?>(checkTableSql, new { Table = tableName }, null, default);
                if (!exists.HasValue)
                {
                    AppendDownloadLog(new DownloadLogEntry
                    {
                        Level = DownloadLogLevel.Warning,
                        Message = $"表 {tableName} 不存在"
                    });
                    return;
                }

                // 检查数据完整性
                var gapSql = $@"SELECT 
    FROM_UNIXTIME(open_time/1000) as gap_start_time,
    FROM_UNIXTIME(next_time/1000) as gap_end_time,
    (next_time - open_time) / 60000 as gap_minutes
FROM (
    SELECT 
        open_time,
        LEAD(open_time) OVER (ORDER BY open_time) as next_time
    FROM `{tableName}`
) t
WHERE next_time IS NOT NULL 
  AND (next_time - open_time) > @ExpectedGap
ORDER BY gap_minutes DESC
LIMIT 20;";

                var gaps = await dbManager.QueryAsync<dynamic>(gapSql, new { ExpectedGap = timeframeMs }, null, default);
                var gapList = gaps.ToList();

                if (gapList.Count == 0)
                {
                    AppendDownloadLog(new DownloadLogEntry
                    {
                        Level = DownloadLogLevel.Info,
                        Message = "数据完整性检查通过，未发现缺失"
                    });

                    // 没有缺失，禁用补齐按钮
                    if (InvokeRequired)
                    {
                        BeginInvoke(() => _fillGapsButton.Enabled = false);
                    }
                    else
                    {
                        _fillGapsButton.Enabled = false;
                    }
                    _detectedGaps.Clear();
                }
                else
                {
                    AppendDownloadLog(new DownloadLogEntry
                    {
                        Level = DownloadLogLevel.Warning,
                        Message = $"发现 {gapList.Count} 处数据缺失："
                    });
                    foreach (var gap in gapList)
                    {
                        AppendDownloadLog(new DownloadLogEntry
                        {
                            Level = DownloadLogLevel.Warning,
                            Message = $"缺失: {gap.gap_start_time} -> {gap.gap_end_time} (约{gap.gap_minutes:F1} 分钟)"
                        });
                    }

                    // 显示尝试补齐按钮提示（仅对 1m 周期显示）
                    AppendDownloadLog(new DownloadLogEntry
                    {
                        Level = DownloadLogLevel.Info,
                        Message = ">>> 点击下方“尝试补齐数据”按钮，将自动下载缺失时间段前后一天的数据尝试补齐（仅处理1分钟周期）<<<"
                    });

                    // 保存缺失信息供补齐功能使用（只保存 1m 周期的缺失）
                    _detectedGaps = gapList.ToList();

                    // 启用补齐按钮（仅对 1m 周期启用）
                    if (InvokeRequired)
                    {
                        BeginInvoke(() => _fillGapsButton.Enabled = true);
                    }
                    else
                    {
                        _fillGapsButton.Enabled = true;
                    }
                }

                // 检查重复
                var duplicateSql = $@"SELECT COUNT(*) - COUNT(DISTINCT open_time) as duplicate_count
FROM `{tableName}`;";
                var duplicateCount = await dbManager.QuerySingleOrDefaultAsync<long>(duplicateSql, null, null, default);
                if (duplicateCount > 0)
                {
                    AppendDownloadLog(new DownloadLogEntry
                    {
                        Level = DownloadLogLevel.Warning,
                        Message = $"发现 {duplicateCount} 条重复记录"
                    });
                }
            }
            catch (Exception ex)
            {
                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Error,
                    Message = $"检查失败: {ex.Message}"
                });
            }
        }

        private async Task CheckAllTimeframesIntegrityAsync()
        {
            if (_downloadSymbol.SelectedItem is not MarketDataConfig.SymbolEnum symbolEnum)
            {
                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Warning,
                    Message = "请先选择币种"
                });
                return;
            }

            try
            {
                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Info,
                    Message = "开始检查所有周期数据完整性..."
                });

                var dbManager = _serviceProvider.GetRequiredService<IDbManager>();
                var exchangeId = MarketDataConfig.ExchangeToString(MarketDataConfig.ExchangeEnum.Binance);
                var symbolStr = MarketDataConfig.SymbolToString(symbolEnum);

                // 获取所有周期（从枚举）
                var allTimeframes = Enum.GetValues<MarketDataConfig.TimeframeEnum>()
                    .Select(tf => MarketDataConfig.TimeframeToString(tf))
                    .ToList();

                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Info,
                    Message = $"将检查以下周期: {string.Join(", ", allTimeframes)}"
                });

                int totalGaps = 0;
                int totalDuplicates = 0;
                int checkedCount = 0;

                foreach (var tf in allTimeframes)
                {
                    try
                    {
                        var tableName = BuildTableName(exchangeId, symbolStr, tf);
                        var timeframeMs = MarketDataConfig.TimeframeToMs(tf);

                        // 检查表是否存在
                        var checkTableSql = @"SELECT 1 FROM information_schema.tables
WHERE table_schema = DATABASE() AND table_name = @Table
LIMIT 1;";
                        var exists = await dbManager.QuerySingleOrDefaultAsync<int?>(checkTableSql, new { Table = tableName }, null, default);
                        if (!exists.HasValue)
                        {
                            AppendDownloadLog(new DownloadLogEntry
                            {
                                Level = DownloadLogLevel.Warning,
                                Message = $"[{tf}] 表 {tableName} 不存在，跳过"
                            });
                            continue;
                        }

                        checkedCount++;

                        // 检查数据完整性
                        var gapSql = $@"SELECT 
    FROM_UNIXTIME(open_time/1000) as gap_start_time,
    FROM_UNIXTIME(next_time/1000) as gap_end_time,
    (next_time - open_time) / 60000 as gap_minutes
FROM (
    SELECT 
        open_time,
        LEAD(open_time) OVER (ORDER BY open_time) as next_time
    FROM `{tableName}`
) t
WHERE next_time IS NOT NULL 
  AND (next_time - open_time) > @ExpectedGap
ORDER BY gap_minutes DESC
LIMIT 20;";

                        var gaps = await dbManager.QueryAsync<dynamic>(gapSql, new { ExpectedGap = timeframeMs }, null, default);
                        var gapList = gaps.ToList();

                        if (gapList.Count > 0)
                        {
                            totalGaps += gapList.Count;
                            AppendDownloadLog(new DownloadLogEntry
                            {
                                Level = DownloadLogLevel.Warning,
                                Message = $"[{tf}] 发现 {gapList.Count} 处数据缺失："
                            });
                            foreach (var gap in gapList.Take(5)) // 只显示前 5 个
                            {
                                AppendDownloadLog(new DownloadLogEntry
                                {
                                    Level = DownloadLogLevel.Warning,
                                    Message = $"[{tf}] 缺失: {gap.gap_start_time} -> {gap.gap_end_time} (约{gap.gap_minutes:F1} 分钟)"
                                });
                            }
                            if (gapList.Count > 5)
                            {
                                AppendDownloadLog(new DownloadLogEntry
                                {
                                    Level = DownloadLogLevel.Warning,
                                    Message = $"[{tf}] ... 还有 {gapList.Count - 5} 处缺失未显示"
                                });
                            }
                        }

                        // 检查重复
                        var duplicateSql = $@"SELECT COUNT(*) - COUNT(DISTINCT open_time) as duplicate_count
FROM `{tableName}`;";
                        var duplicateCount = await dbManager.QuerySingleOrDefaultAsync<long>(duplicateSql, null, null, default);
                        if (duplicateCount > 0)
                        {
                            totalDuplicates += (int)duplicateCount;
                            AppendDownloadLog(new DownloadLogEntry
                            {
                                Level = DownloadLogLevel.Warning,
                                Message = $"[{tf}] 发现 {duplicateCount} 条重复记录"
                            });
                        }
                        else
                        {
                            AppendDownloadLog(new DownloadLogEntry
                            {
                                Level = DownloadLogLevel.Info,
                                Message = $"[{tf}] 数据完整，无缺失无重复"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendDownloadLog(new DownloadLogEntry
                        {
                            Level = DownloadLogLevel.Error,
                            Message = $"[{tf}] 检查失败: {ex.Message}"
                        });
                    }
                }

                // 总结
                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = totalGaps == 0 && totalDuplicates == 0 ? DownloadLogLevel.Info : DownloadLogLevel.Warning,
                    Message = $"所有周期检查完成：检查了 {checkedCount} 个周期，共发现 {totalGaps} 处缺失，{totalDuplicates} 条重复记录"
                });

                if (totalGaps > 0 || totalDuplicates > 0)
                {
                    AppendDownloadLog(new DownloadLogEntry
                    {
                        Level = DownloadLogLevel.Info,
                        Message = "提示: 如果1分钟周期数据完整，可使用“同步聚合其他周期”功能重新生成其他周期数据"
                    });
                }
            }
            catch (Exception ex)
            {
                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Error,
                    Message = $"检查失败: {ex.Message}"
                });
            }
        }

        private async Task AggregateTimeframesAsync()
        {
            if (_downloadSymbol.SelectedItem is not MarketDataConfig.SymbolEnum symbolEnum)
            {
                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Warning,
                    Message = "Please select symbol first."
                });
                return;
            }

            try
            {
                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Info,
                    Message = "Start aggregating other timeframes..."
                });

                var dbManager = _serviceProvider.GetRequiredService<IDbManager>();
                var exchangeId = MarketDataConfig.ExchangeToString(MarketDataConfig.ExchangeEnum.Binance);
                var symbolStr = MarketDataConfig.SymbolToString(symbolEnum);
                var tableName1m = BuildTableName(exchangeId, symbolStr, "1m");

                var checkTableSql = @"SELECT 1 FROM information_schema.tables
WHERE table_schema = DATABASE() AND table_name = @Table
LIMIT 1;";
                var exists = await dbManager.QuerySingleOrDefaultAsync<int?>(checkTableSql, new { Table = tableName1m }, null, default);
                if (!exists.HasValue)
                {
                    AppendDownloadLog(new DownloadLogEntry
                    {
                        Level = DownloadLogLevel.Warning,
                        Message = $"1m table {tableName1m} not found."
                    });
                    return;
                }

                var countSql = $@"SELECT COUNT(*) as count FROM `{tableName1m}`;";
                var totalCount = await dbManager.QuerySingleOrDefaultAsync<long>(countSql, null, null, default);
                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Info,
                    Message = $"1m table {tableName1m} rows: {totalCount}."
                });

                if (totalCount == 0)
                {
                    AppendDownloadLog(new DownloadLogEntry
                    {
                        Level = DownloadLogLevel.Warning,
                        Message = "1m table has no rows, skip aggregation."
                    });
                    return;
                }

                var rangeSql = $@"SELECT MIN(open_time) AS Min, MAX(open_time) AS Max FROM `{tableName1m}`;";
                var timeRange = await dbManager.QuerySingleOrDefaultAsync<OpenTimeRange>(rangeSql, null, null, default);
                if (timeRange == null || timeRange.Max <= 0)
                {
                    AppendDownloadLog(new DownloadLogEntry
                    {
                        Level = DownloadLogLevel.Warning,
                        Message = "Failed to read time range from 1m table."
                    });
                    return;
                }

                var allTimeframes = Enum.GetValues<MarketDataConfig.TimeframeEnum>()
                    .Where(tf => tf != MarketDataConfig.TimeframeEnum.m1)
                    .Select(tf => MarketDataConfig.TimeframeToString(tf))
                    .ToList();

                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Info,
                    Message = $"Aggregate timeframes: {string.Join(", ", allTimeframes)}"
                });

                foreach (var tf in allTimeframes)
                {
                    try
                    {
                        AppendDownloadLog(new DownloadLogEntry
                        {
                            Level = DownloadLogLevel.Info,
                            Message = $"Start aggregating {tf}..."
                        });

                        var targetTableName = BuildTableName(exchangeId, symbolStr, tf);
                        var tfMs = MarketDataConfig.TimeframeToMs(tf);
                        var expectedRows = totalCount > 0 ? (long)(totalCount * 60 * 1000 / tfMs) : 0;

                        AppendDownloadLog(new DownloadLogEntry
                        {
                            Level = DownloadLogLevel.Info,
                            Message = $"Target table: {targetTableName}, tfMs: {tfMs}, expected: {expectedRows} rows"
                        });

                        AppendDownloadLog(new DownloadLogEntry
                        {
                            Level = DownloadLogLevel.Info,
                            Message = $"Create/check table: {targetTableName}"
                        });
                        var createTableSql = $@"CREATE TABLE IF NOT EXISTS `{targetTableName}` LIKE `{tableName1m}`;";
                        await dbManager.ExecuteAsync(createTableSql, null, null, default);

                        var existingCountSql = $@"SELECT COUNT(*) as count FROM `{targetTableName}`;";
                        var existingCount = await dbManager.QuerySingleOrDefaultAsync<long>(existingCountSql, null, null, default);
                        AppendDownloadLog(new DownloadLogEntry
                        {
                            Level = DownloadLogLevel.Info,
                            Message = $"Target table {targetTableName} has {existingCount} rows."
                        });

                        AppendDownloadLog(new DownloadLogEntry
                        {
                            Level = DownloadLogLevel.Info,
                            Message = $"Run aggregation SQL from {tableName1m} to {targetTableName}..."
                        });

                        var startTime = DateTime.UtcNow;

                        var aggregateSql = $@"INSERT IGNORE INTO `{targetTableName}`
(open_time, open, high, low, close, volume, close_time, quote_volume, count, taker_buy_volume, taker_buy_quote_volume, ignore_col)
SELECT
    agg.bucket_time as open_time,
    first_row.open as open,
    agg.max_high as high,
    agg.min_low as low,
    last_row.close as close,
    agg.sum_volume as volume,
    agg.max_close_time as close_time,
    agg.sum_quote_volume as quote_volume,
    agg.sum_count as count,
    agg.sum_taker_buy_volume as taker_buy_volume,
    agg.sum_taker_buy_quote_volume as taker_buy_quote_volume,
    agg.max_ignore_col as ignore_col
FROM (
    SELECT
        FLOOR(open_time / @TfMs) * @TfMs as bucket_time,
        MAX(high) as max_high,
        MIN(low) as min_low,
        SUM(volume) as sum_volume,
        MAX(close_time) as max_close_time,
        SUM(quote_volume) as sum_quote_volume,
        SUM(count) as sum_count,
        SUM(taker_buy_volume) as sum_taker_buy_volume,
        SUM(taker_buy_quote_volume) as sum_taker_buy_quote_volume,
        MAX(ignore_col) as max_ignore_col,
        MIN(open_time) as min_open_time,
        MAX(open_time) as max_open_time
    FROM `{tableName1m}`
    WHERE open_time >= @Start AND open_time < @End
    GROUP BY bucket_time
) agg
INNER JOIN `{tableName1m}` first_row ON first_row.open_time = agg.min_open_time AND FLOOR(first_row.open_time / @TfMs) * @TfMs = agg.bucket_time
INNER JOIN `{tableName1m}` last_row ON last_row.open_time = agg.max_open_time AND FLOOR(last_row.open_time / @TfMs) * @TfMs = agg.bucket_time
;";

                        var rangeStart = AlignDown(timeRange.Min, tfMs);
                        var rangeEndExclusive = AlignUp(timeRange.Max + 1, tfMs);
                        var batchMs = Math.Max((long)TimeSpan.FromDays(7).TotalMilliseconds, tfMs);
                        batchMs = AlignDown(batchMs, tfMs);
                        if (batchMs <= 0)
                        {
                            batchMs = tfMs;
                        }

                        var totalInserted = 0;
                        for (var start = rangeStart; start < rangeEndExclusive; start += batchMs)
                        {
                            var end = Math.Min(start + batchMs, rangeEndExclusive);
                            AppendDownloadLog(new DownloadLogEntry
                            {
                                Level = DownloadLogLevel.Info,
                                Message = $"Aggregating {tf} batch: {start} ~ {end}"
                            });

                            var batchInserted = await dbManager.ExecuteAsync(
                                aggregateSql,
                                new { TfMs = tfMs, Start = start, End = end },
                                null,
                                default);
                            totalInserted += batchInserted;
                        }

                        var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;

                        AppendDownloadLog(new DownloadLogEntry
                        {
                            Level = DownloadLogLevel.Info,
                            Message = $"OK {tf} aggregation done. Inserted {totalInserted} rows in {elapsed:F2}s."
                        });
                    }
                    catch (Exception ex)
                    {
                        var errorDetails = ex is TimeoutException
                            ? "Timeout. Possible causes: huge data, complex SQL, low timeout. Consider smaller batch or higher timeout."
                            : $"ErrorType: {ex.GetType().Name}";

                        AppendDownloadLog(new DownloadLogEntry
                        {
                            Level = DownloadLogLevel.Error,
                            Message = $"FAIL {tf} aggregation: {ex.Message}"
                        });
                        AppendDownloadLog(new DownloadLogEntry
                        {
                            Level = DownloadLogLevel.Warning,
                            Message = $"    {errorDetails}"
                        });
                        if (ex.InnerException != null)
                        {
                            AppendDownloadLog(new DownloadLogEntry
                            {
                                Level = DownloadLogLevel.Warning,
                                Message = $"    Inner: {ex.InnerException.Message}"
                            });
                        }
                    }
                }

                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Info,
                    Message = "All timeframes aggregation done."
                });
            }
            catch (Exception ex)
            {
                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Error,
                    Message = $"Aggregation failed: {ex.Message}"
                });
            }
        }

        private static long AlignDown(long value, long step)
        {
            if (step <= 0)
            {
                return value;
            }

            return (value / step) * step;
        }

        private static long AlignUp(long value, long step)
        {
            if (step <= 0)
            {
                return value;
            }

            return ((value + step - 1) / step) * step;
        }

        private sealed class OpenTimeRange
        {
            public long Min { get; set; }
            public long Max { get; set; }
        }

        private async Task TryFillGapsAsync()
        {
            if (_downloadSymbol.SelectedItem is not MarketDataConfig.SymbolEnum symbolEnum)
            {
                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Warning,
                    Message = "请先选择币种"
                });
                return;
            }

            if (_detectedGaps.Count == 0)
            {
                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Warning,
                    Message = "没有检测到数据缺失，请先运行验证基础数据完整性"
                });
                return;
            }

            try
            {
                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Info,
                    Message = $"开始尝试补齐 {_detectedGaps.Count} 处数据缺失（仅处理 1 分钟周期）..."
                });

                var dbManager = _serviceProvider.GetRequiredService<IDbManager>();
                var exchangeId = MarketDataConfig.ExchangeToString(MarketDataConfig.ExchangeEnum.Binance);
                var symbolStr = MarketDataConfig.SymbolToString(symbolEnum);
                var timeframeStr = "1m"; // 固定 1 分钟周期
                var timeframeEnum = MarketDataConfig.TimeframeEnum.m1; // 固定 1 分钟周期
                var tableName = BuildTableName(exchangeId, symbolStr, timeframeStr);
                var timeframeMs = MarketDataConfig.TimeframeToMs(timeframeStr);

                int successCount = 0;
                int failCount = 0;

                foreach (var gap in _detectedGaps)
                {
                    try
                    {
                        var gapStart = DateTime.Parse(gap.gap_start_time.ToString());
                        var gapEnd = DateTime.Parse(gap.gap_end_time.ToString());
                        // 计算下载范围：前一天到后一天
                        var downloadStart = gapStart.AddDays(-1).Date;
                        var downloadEnd = gapEnd.AddDays(1).Date;

                        AppendDownloadLog(new DownloadLogEntry
                        {
                            Level = DownloadLogLevel.Info,
                            Message = $"尝试补齐: {gapStart:yyyy-MM-dd HH:mm} -> {gapEnd:yyyy-MM-dd HH:mm}，下载范围 {downloadStart:yyyy-MM-dd} 至 {downloadEnd:yyyy-MM-dd}"
                        });

                        // 使用下载器下载数据（强制日线下载，限制结束日期）
                        var cts = new CancellationTokenSource();
                        Action<DownloadLogEntry> logAction = AppendDownloadLog;
                        var summary = await _downloader.DownloadAsync(
                            symbolEnum,
                            timeframeEnum,
                            downloadStart,
                            logAction,
                            cts.Token,
                            forceDailyOnly: true,
                            endDate: downloadEnd);

                        // 等待一段让数据写入完成
                        await Task.Delay(1000, default);
                        // 检查是否补齐成功
                        var checkGapSql = $@"SELECT COUNT(*) as count
FROM `{tableName}`
WHERE open_time >= @GapStartMs AND open_time < @GapEndMs;";

                        var gapStartMs = new DateTimeOffset(gapStart).ToUnixTimeMilliseconds();
                        var gapEndMs = new DateTimeOffset(gapEnd).ToUnixTimeMilliseconds();

                        var countResult = await dbManager.QuerySingleOrDefaultAsync<dynamic>(
                            checkGapSql,
                            new { GapStartMs = gapStartMs, GapEndMs = gapEndMs },
                            null,
                            default);

                        var filledCount = countResult != null ? Convert.ToInt64(countResult.count) : 0;
                        var expectedCount = (long)((gapEndMs - gapStartMs) / timeframeMs);

                        if (filledCount >= expectedCount * 0.9) // 如果补齐 90% 以上认为成功
                        {
                            AppendDownloadLog(new DownloadLogEntry
                            {
                                Level = DownloadLogLevel.Info,
                                Message = $"补齐成功: {gapStart:yyyy-MM-dd HH:mm} -> {gapEnd:yyyy-MM-dd HH:mm}，补齐了 {filledCount}/{expectedCount} 条记录"
                            });
                            successCount++;
                        }
                        else
                        {
                            AppendDownloadLog(new DownloadLogEntry
                            {
                                Level = DownloadLogLevel.Warning,
                                Message = $"补齐失败: {gapStart:yyyy-MM-dd HH:mm} -> {gapEnd:yyyy-MM-dd HH:mm}，仅补齐 {filledCount}/{expectedCount} 条记录"
                            });
                            failCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendDownloadLog(new DownloadLogEntry
                        {
                            Level = DownloadLogLevel.Error,
                            Message = $"补齐过程出错: {ex.Message}"
                        });
                        failCount++;
                    }
                }

                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = successCount > 0 ? DownloadLogLevel.Info : DownloadLogLevel.Warning,
                    Message = $"补齐完成: 成功 {successCount} 处，失败 {failCount} 处"
                });
                // 重新检查基础数据完整性
                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Info,
                    Message = "重新检查基础数据完整性..."
                });
                await CheckBaseDataIntegrityAsync();
            }
            catch (Exception ex)
            {
                AppendDownloadLog(new DownloadLogEntry
                {
                    Level = DownloadLogLevel.Error,
                    Message = $"琛ラ綈澶辫触: {ex.Message}"
                });
            }
        }

        private static string BuildTableName(string exchangeId, string symbolStr, string timeframeStr)
        {
            var exchangeKey = MarketDataKeyNormalizer.NormalizeExchange(exchangeId);
            var symbolKey = MarketDataKeyNormalizer.NormalizeSymbol(symbolStr);
            var timeframeKey = MarketDataKeyNormalizer.NormalizeTimeframe(timeframeStr);
            var symbolPart = symbolKey.Replace("/", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            return $"{exchangeKey}_futures_{symbolPart}_{timeframeKey}";
        }
    }
}
