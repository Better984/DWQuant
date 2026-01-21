using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ServerTest.Models;
using ServerTest.Services;
using ServerTest.WebSockets;

namespace ServerTest.Monitoring
{
    public sealed class StartupMonitorForm : Form
    {
        private const int MaxLogItems = 1000;
        private readonly Func<IReadOnlyList<HistoricalCacheSnapshot>> _historyProvider;
        private readonly Func<IReadOnlyList<WebSocketConnection>> _connectionsProvider;
        private readonly BinanceHistoricalDataDownloader _downloader;

        private ListView _logList = null!;
        private ListView _statusList = null!;
        private CheckedListBox _sourceFilterList = null!;
        private Label _readyCount = null!;
        private Label _startingCount = null!;
        private Label _failedCount = null!;
        private Panel _outputPanel = null!;
        private Panel _historyPanel = null!;
        private Panel _networkPanel = null!;
        private Panel _downloadPanel = null!;
        private ListView _historyList = null!;
        private ListView _networkUserList = null!;
        private Label _historySummaryLabel = null!;
        private Label _networkSummaryLabel = null!;
        private Button _toggleOutputButton = null!;
        private Button _toggleHistoryButton = null!;
        private Button _toggleNetworkButton = null!;
        private Button _toggleDownloadButton = null!;
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
        private CancellationTokenSource? _downloadCts;
        private bool _downloadRunning;
        private ContextMenuStrip _downloadLogMenu = null!;

        private readonly Dictionary<SystemModule, ListViewItem> _statusItems = new();
        private readonly List<StartupMonitorLogEntry> _logEntries = new();
        private readonly HashSet<string> _selectedSources = new(StringComparer.OrdinalIgnoreCase);
        private bool _suppressSourceFilterEvents;

        public StartupMonitorForm(
            Func<IReadOnlyList<HistoricalCacheSnapshot>> historyProvider,
            Func<IReadOnlyList<WebSocketConnection>> connectionsProvider,
            BinanceHistoricalDataDownloader downloader)
        {
            _historyProvider = historyProvider ?? (() => Array.Empty<HistoricalCacheSnapshot>());
            _connectionsProvider = connectionsProvider ?? (() => Array.Empty<WebSocketConnection>());
            _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));

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

            _readyCount = (Label)header.Controls.Find("ReadyCount", true).First();
            _startingCount = (Label)header.Controls.Find("StartingCount", true).First();
            _failedCount = (Label)header.Controls.Find("FailedCount", true).First();

            _refreshTimer = new System.Windows.Forms.Timer
            {
                Interval = 2000
            };
            _refreshTimer.Tick += (_, __) =>
            {
                RefreshHistoryData();
                RefreshNetworkData();
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
            _logEntries.Add(entry);

            if (_logEntries.Count > MaxLogItems)
            {
                _logEntries.RemoveAt(0);
            }

            if (_selectedSources.Contains(entry.Category))
            {
                AppendLogItem(entry);
                _logList.EnsureVisible(_logList.Items.Count - 1);
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
            Download
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
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 18, 0, 0),
                WrapContents = false
            };

            _toggleOutputButton = BuildToggleButton("输出");
            _toggleOutputButton.Click += (_, __) => SetActiveView(ViewMode.Output);
            _toggleHistoryButton = BuildToggleButton("历史行情");
            _toggleHistoryButton.Click += (_, __) => SetActiveView(ViewMode.History);
            _toggleNetworkButton = BuildToggleButton("网络详情");
            _toggleNetworkButton.Click += (_, __) => SetActiveView(ViewMode.Network);
            _toggleDownloadButton = BuildToggleButton("数据下载");
            _toggleDownloadButton.Click += (_, __) => SetActiveView(ViewMode.Download);

            togglePanel.Controls.Add(_toggleOutputButton);
            togglePanel.Controls.Add(_toggleHistoryButton);
            togglePanel.Controls.Add(_toggleNetworkButton);
            togglePanel.Controls.Add(_toggleDownloadButton);

            var summaryPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 20, 0, 0),
            };

            summaryPanel.Controls.Add(BuildSummaryChip("✅ 就绪", "ReadyCount", Color.FromArgb(30, 150, 85)));
            summaryPanel.Controls.Add(BuildSummaryChip("⏳ 启动中", "StartingCount", Color.FromArgb(242, 170, 35)));
            summaryPanel.Controls.Add(BuildSummaryChip("❌ 失败", "FailedCount", Color.FromArgb(220, 70, 70)));

            var headerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
            };
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));

            headerLayout.Controls.Add(titlePanel, 0, 0);
            headerLayout.Controls.Add(togglePanel, 1, 0);
            headerLayout.Controls.Add(summaryPanel, 2, 0);

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

            container.Controls.Add(_outputPanel);
            container.Controls.Add(_historyPanel);
            container.Controls.Add(_networkPanel);
            container.Controls.Add(_downloadPanel);

            _outputPanel.Dock = DockStyle.Fill;
            _historyPanel.Dock = DockStyle.Fill;
            _networkPanel.Dock = DockStyle.Fill;
            _downloadPanel.Dock = DockStyle.Fill;
            _outputPanel.Visible = false;
            _historyPanel.Visible = false;
            _networkPanel.Visible = false;
            _downloadPanel.Visible = false;

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

        private Panel BuildHistoryPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16)
            };

            var title = new Label
            {
                Text = "历史行情缓存",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(6, 4)
            };

            _historySummaryLabel = new Label
            {
                Text = "缓存项: 0",
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                AutoSize = true,
                ForeColor = Color.FromArgb(90, 90, 90),
                Location = new Point(8, 30)
            };

            _historyList = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Dock = DockStyle.Bottom,
                Height = 400
            };
            _historyList.Columns.Add("交易所", 120);
            _historyList.Columns.Add("币对", 140);
            _historyList.Columns.Add("周期", 80);
            _historyList.Columns.Add("开始时间", 180);
            _historyList.Columns.Add("结束时间", 180);
            _historyList.Columns.Add("缓存数量", 90);

            panel.Controls.Add(title);
            panel.Controls.Add(_historySummaryLabel);
            panel.Controls.Add(_historyList);
            _historyList.Dock = DockStyle.Fill;
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
                Text = "Binance Futures 下载日线 ZIP，遇到首次有数据后继续至今天",
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
                RowCount = 2
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

            formPanel.Controls.Add(symbolLabel, 0, 0);
            formPanel.Controls.Add(_downloadSymbol, 1, 0);
            formPanel.Controls.Add(tfLabel, 2, 0);
            formPanel.Controls.Add(_downloadTimeframe, 3, 0);
            formPanel.Controls.Add(startLabel, 4, 0);
            formPanel.Controls.Add(_downloadStartDate, 5, 0);
            formPanel.Controls.Add(_downloadStartButton, 1, 1);
            formPanel.Controls.Add(_downloadCancelButton, 2, 1);

            _downloadStatusLabel = new Label
            {
                Text = "状态 待机",
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

            var sourcePanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 200,
            };

            var filterLabel = new Label
            {
                Text = "Sources",
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                Dock = DockStyle.Top,
                Height = 22,
            };

            var filterActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 2, 0, 4),
                WrapContents = false,
            };

            var selectAllButton = new Button
            {
                Text = "All",
                Width = 70,
                Height = 24,
            };
            selectAllButton.Click += (_, __) => SetSourceFilterSelection(true);

            var selectNoneButton = new Button
            {
                Text = "None",
                Width = 70,
                Height = 24,
            };
            selectNoneButton.Click += (_, __) => SetSourceFilterSelection(false);

            filterActions.Controls.Add(selectAllButton);
            filterActions.Controls.Add(selectNoneButton);

            var filterList = new CheckedListBox
            {
                Name = "SourceFilterList",
                CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false,
                Dock = DockStyle.Fill,
            };
            filterList.ItemCheck += (_, __) =>
            {
                if (_suppressSourceFilterEvents)
                {
                    return;
                }

                if (IsHandleCreated)
                {
                    BeginInvoke(new Action(ApplySourceFilter));
                }
                else
                {
                    ApplySourceFilter();
                }
            };

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

            _sourceFilterList = filterList;
            _logList = list;

            panel.Controls.Add(title);
            panel.Controls.Add(contentPanel);
            sourcePanel.Controls.Add(filterList);
            sourcePanel.Controls.Add(filterActions);
            sourcePanel.Controls.Add(filterLabel);
            contentPanel.Controls.Add(list);
            contentPanel.Controls.Add(sourcePanel);
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
                SystemStatus.Ready => "✅ 就绪",
                SystemStatus.Starting => "⏳ 启动中",
                SystemStatus.Failed => "❌ 失败",
                _ => "⚪ 未启动"
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

        private void ApplySourceFilter()
        {
            _selectedSources.Clear();
            foreach (var item in _sourceFilterList.CheckedItems)
            {
                if (item is string source)
                {
                    _selectedSources.Add(source);
                }
            }

            _logList.BeginUpdate();
            _logList.Items.Clear();
            foreach (var entry in _logEntries)
            {
                if (_selectedSources.Contains(entry.Category))
                {
                    AppendLogItem(entry);
                }
            }
            _logList.EndUpdate();
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
            else
            {
                _downloadPanel.BringToFront();
            }

            ApplyToggleStyle(_toggleOutputButton, mode == ViewMode.Output);
            ApplyToggleStyle(_toggleHistoryButton, mode == ViewMode.History);
            ApplyToggleStyle(_toggleNetworkButton, mode == ViewMode.Network);
            ApplyToggleStyle(_toggleDownloadButton, mode == ViewMode.Download);

            if (mode == ViewMode.History)
            {
                RefreshHistoryData();
            }
            else if (mode == ViewMode.Network)
            {
                RefreshNetworkData();
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

            _historyList.BeginUpdate();
            _historyList.Items.Clear();
            foreach (var item in data)
            {
                var row = new ListViewItem(item.Exchange);
                row.SubItems.Add(item.Symbol);
                row.SubItems.Add(item.Timeframe);
                row.SubItems.Add(item.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
                row.SubItems.Add(item.EndTime.ToString("yyyy-MM-dd HH:mm:ss"));
                row.SubItems.Add(item.Count.ToString());
                _historyList.Items.Add(row);
            }
            _historyList.EndUpdate();

            _historySummaryLabel.Text = $"缓存项: {data.Count}";
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

            _networkSummaryLabel.Text = $"WebSocket 连接: {connections.Count} | 用户数: {userGroups.Count}";

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
            _downloadStatusLabel.Text = "状态 进行中...";

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
                var summary = await Task.Run(
                    () => _downloader.DownloadAsync(symbolEnum, timeframeEnum, startDate, AppendDownloadLog, _downloadCts.Token),
                    _downloadCts.Token);

                _downloadStatusLabel.Text = $"状态 完成, 天数={summary.DaysProcessed}, 下载={summary.DaysDownloaded}, 入库={summary.RowsInserted}";
            }
            catch (OperationCanceledException)
            {
                _downloadStatusLabel.Text = "状态 已停止";
            }
            catch (Exception ex)
            {
                _downloadStatusLabel.Text = "状态 失败";
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
            var ready = 0;
            var starting = 0;
            var failed = 0;

            foreach (var item in _statusItems.Values)
            {
                var statusText = item.SubItems[1].Text;
                if (statusText.Contains("就绪"))
                {
                    ready++;
                }
                else if (statusText.Contains("启动中"))
                {
                    starting++;
                }
                else if (statusText.Contains("失败"))
                {
                    failed++;
                }
            }

            _readyCount.Text = ready.ToString();
            _startingCount.Text = starting.ToString();
            _failedCount.Text = failed.ToString();
        }
    }
}
