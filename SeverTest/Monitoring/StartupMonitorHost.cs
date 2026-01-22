using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using ServerTest.Services;
using ServerTest.WebSockets;

namespace ServerTest.Monitoring
{
    public sealed class StartupMonitorHost
    {
        private readonly object _sync = new();
        private readonly ConcurrentQueue<StartupMonitorLogEntry> _pendingLogs = new();
        private readonly IServiceProvider _serviceProvider;
        private StartupMonitorForm? _form;
        private Thread? _uiThread;
        private bool _started;

        public StartupMonitorHost(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void Start(SystemStartupManager startupManager)
        {
            if (startupManager == null)
            {
                throw new ArgumentNullException(nameof(startupManager));
            }

            lock (_sync)
            {
                if (_started)
                {
                    return;
                }

                _started = true;
            }

            _uiThread = new Thread(() =>
            {
                System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.SystemAware);
                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

                var historicalCache = _serviceProvider.GetRequiredService<HistoricalMarketDataCache>();
                var connectionManager = _serviceProvider.GetRequiredService<IConnectionManager>();
                var downloader = _serviceProvider.GetRequiredService<BinanceHistoricalDataDownloader>();
                var form = new StartupMonitorForm(
                    () => historicalCache.GetCacheSnapshots(),
                    () => connectionManager.GetAllConnections(),
                    downloader,
                    _serviceProvider);
                _form = form;

                var initialStatuses = startupManager.GetAllStatuses();
                form.LoadStatuses(initialStatuses);
                DrainPendingLogs(form);

                startupManager.StatusChanged += OnStatusChanged;

                System.Windows.Forms.Application.Run(form);

                startupManager.StatusChanged -= OnStatusChanged;
            })
            {
                IsBackground = true
            };
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();
        }

        public void AppendLog(StartupMonitorLogEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            var form = _form;
            if (form == null)
            {
                _pendingLogs.Enqueue(entry);
                return;
            }

            form.AppendLog(entry);
        }

        private void OnStatusChanged(object? sender, SystemStatusChangedEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            _form?.UpdateStatus(e.Module, e.Status, e.ErrorMessage);
        }

        private void DrainPendingLogs(StartupMonitorForm form)
        {
            while (_pendingLogs.TryDequeue(out var entry))
            {
                form.AppendLog(entry);
            }
        }
    }
}
