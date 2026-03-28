using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ResourceRouter.App.Services;
using ResourceRouter.App.Views;
using ResourceRouter.Infrastructure.Logging;
using Forms = System.Windows.Forms;

namespace ResourceRouter.App
{
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = "ResourceRouter.App.SingleInstance";
        private const string ActivateSignalName = "ResourceRouter.App.ActivateSignal";

        private FileLogger? _logger;
        private AppRuntime? _runtime;
        private Mutex? _singleInstanceMutex;
        private bool _ownsMutex;
        private Forms.NotifyIcon? _notifyIcon;
        private EventWaitHandle? _activateSignal;
        private RegisteredWaitHandle? _activateSignalRegistration;

        private async void OnStartup(object sender, StartupEventArgs e)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateSignalName);

            var createdNew = false;
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
            _ownsMutex = createdNew;
            if (!createdNew)
            {
                TrySignalRunningInstance();
                Shutdown();
                return;
            }

            _logger = new FileLogger();
            RegisterGlobalExceptionHandlers(_logger);

            _runtime = new AppRuntime(_logger);
            await _runtime.InitializeAsync().ConfigureAwait(true);

            var edgeBarWindow = new EdgeBarWindow(_runtime);
            MainWindow = edgeBarWindow;
            edgeBarWindow.Show();

            RegisterActivationSignal(edgeBarWindow);
            InitializeTray(edgeBarWindow);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_notifyIcon is not null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            _runtime?.Dispose();
            if (_ownsMutex)
            {
                _singleInstanceMutex?.ReleaseMutex();
            }

            _activateSignalRegistration?.Unregister(null);
            _activateSignal?.Dispose();

            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }

        private void InitializeTray(Window edgeBarWindow)
        {
            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "Resource Router",
                Visible = true
            };

            var menu = new Forms.ContextMenuStrip();
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = false;
            menu.BackColor = Color.FromArgb(0x13, 0x1A, 0x21);
            menu.ForeColor = Color.FromArgb(0xE8, 0xEE, 0xF5);
            menu.RenderMode = Forms.ToolStripRenderMode.Professional;
            menu.Renderer = new Forms.ToolStripProfessionalRenderer(new TrayMenuColorTable());

            var togglePanelItem = new Forms.ToolStripMenuItem("展开主面板");
            togglePanelItem.Click += (_, _) => Dispatcher.Invoke(() => ToggleMainPanel(edgeBarWindow));
            menu.Opening += (_, _) =>
            {
                togglePanelItem.Text = IsMainPanelExpanded(edgeBarWindow)
                    ? "关闭主面板"
                    : "展开主面板";
            };

            menu.Items.Add(togglePanelItem);
            menu.Items.Add("退出", null, (_, _) => Current.Shutdown());

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.DoubleClick += (_, _) =>
            {
                Dispatcher.Invoke(() => ToggleMainPanel(edgeBarWindow));
            };
        }

        private static void ToggleMainPanel(Window edgeBarWindow)
        {
            if (edgeBarWindow is EdgeBarWindow edgeBar)
            {
                edgeBar.ToggleMainPanelFromTray();
                return;
            }

            ActivateWindow(edgeBarWindow);
        }

        private static bool IsMainPanelExpanded(Window edgeBarWindow)
        {
            return edgeBarWindow is EdgeBarWindow edgeBar && edgeBar.IsPanelExpanded;
        }

        private void RegisterActivationSignal(Window edgeBarWindow)
        {
            if (_activateSignal is null)
            {
                return;
            }

            _activateSignalRegistration = ThreadPool.RegisterWaitForSingleObject(
                _activateSignal,
                (_, _) => Dispatcher.BeginInvoke(() => ActivateWindow(edgeBarWindow)),
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }

        private static void ActivateWindow(Window window)
        {
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Show();
            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
        }

        private static void TrySignalRunningInstance()
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ActivateSignalName);
                signal.Set();
            }
            catch
            {
                // Ignore when signal cannot be opened.
            }
        }

        private sealed class TrayMenuColorTable : Forms.ProfessionalColorTable
        {
            public override Color MenuBorder => Color.FromArgb(0x3A, 0x44, 0x50);
            public override Color ToolStripDropDownBackground => Color.FromArgb(0x13, 0x1A, 0x21);
            public override Color ImageMarginGradientBegin => Color.FromArgb(0x13, 0x1A, 0x21);
            public override Color ImageMarginGradientMiddle => Color.FromArgb(0x13, 0x1A, 0x21);
            public override Color ImageMarginGradientEnd => Color.FromArgb(0x13, 0x1A, 0x21);
            public override Color MenuItemBorder => Color.FromArgb(0x5A, 0x74, 0x93);
            public override Color MenuItemSelected => Color.FromArgb(0x2A, 0x3B, 0x4E);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(0x2A, 0x3B, 0x4E);
            public override Color MenuItemSelectedGradientEnd => Color.FromArgb(0x2A, 0x3B, 0x4E);
        }

        private static void RegisterGlobalExceptionHandlers(FileLogger logger)
        {
            Current.DispatcherUnhandledException += (_, args) =>
            {
                logger.LogError("DispatcherUnhandledException", args.Exception);
                args.Handled = true;
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                logger.LogError("TaskScheduler.UnobservedTaskException", args.Exception);
                args.SetObserved();
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                logger.LogError("AppDomain.UnhandledException", args.ExceptionObject as Exception);
            };
        }
    }
}
