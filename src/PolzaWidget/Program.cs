using System;
using System.Threading;
using System.Windows.Forms;

namespace PolzaWidget
{
    internal static class Program
    {
        private const string MutexName = "Local\\PolzaWidget";
        private const string ShowEventName = "Local\\PolzaWidgetShow";

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (HasArg(args, "--uninstall"))
            {
                Shell.Uninstall();
                return;
            }

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // Уже запущен: просим показать окно и выходим.
                    SignalShow();
                    return;
                }

                Log.RotateIfNeeded();
                Log.Write("старт");

                AppSettings settings = AppSettings.Load();
                if (settings == null || string.IsNullOrEmpty(settings.apiKey))
                {
                    using (var setup = new SetupForm())
                    {
                        if (setup.ShowDialog() != DialogResult.OK) return;
                        settings = setup.Settings;
                    }
                }

                using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                {
                    var form = new WidgetForm(settings);
                    using (var context = new TrayContext(form, settings, showEvent))
                    {
                        form.Show();
                        form.StartAutoRefresh();
                        Application.Run(context);
                    }
                }
            }
        }

        private static void SignalShow()
        {
            try
            {
                using (EventWaitHandle handle = EventWaitHandle.OpenExisting(ShowEventName))
                    handle.Set();
            }
            catch { }
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;
            foreach (string arg in args)
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    /// <summary>Иконка в трее и меню. Живёт, пока приложение работает.</summary>
    internal sealed class TrayContext : ApplicationContext
    {
        private readonly WidgetForm _form;
        private readonly NotifyIcon _tray;
        private readonly ToolStripMenuItem _topmostItem;
        private readonly ToolStripMenuItem _autoStartItem;
        private readonly ToolStripMenuItem _updateItem;
        private readonly EventWaitHandle _showEvent;
        private readonly Thread _listener;
        private volatile bool _stopping;

        public TrayContext(WidgetForm form, AppSettings settings, EventWaitHandle showEvent)
        {
            _form = form;
            _showEvent = showEvent;

            _tray = new NotifyIcon();
            _tray.Icon = Shell.TrayIcon();
            _tray.Text = "polza.ai — расходы";
            _tray.Visible = true;

            var menu = new ContextMenuStrip();
            var showItem = new ToolStripMenuItem("Показать / скрыть");
            showItem.Click += delegate { _form.ToggleVisible(); };

            _topmostItem = new ToolStripMenuItem("Поверх всех окон");
            _topmostItem.CheckOnClick = true;
            _topmostItem.Checked = settings.topmost;
            _topmostItem.Click += delegate { _form.SetTopmost(_topmostItem.Checked); };

            _autoStartItem = new ToolStripMenuItem("Запускать при входе");
            _autoStartItem.CheckOnClick = true;
            _autoStartItem.Checked = Shell.IsAutoStart();
            _autoStartItem.Click += delegate { Shell.SetAutoStart(_autoStartItem.Checked); };

            _updateItem = new ToolStripMenuItem("Доступна новая версия");
            _updateItem.Visible = false;
            _updateItem.Click += delegate { OpenUpdatePage(); };

            var checkItem = new ToolStripMenuItem("Проверить обновления");
            checkItem.Click += delegate { CheckForUpdates(false); };

            var exitItem = new ToolStripMenuItem("Выход");
            exitItem.Click += delegate { ExitApp(); };

            menu.Items.Add(showItem);
            menu.Items.Add(_topmostItem);
            menu.Items.Add(_autoStartItem);
            menu.Items.Add(_updateItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(checkItem);
            menu.Items.Add(exitItem);

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { _form.ShowFromTray(); };
            _tray.BalloonTipClicked += delegate { OpenUpdatePage(); };
            _tray.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) _form.ToggleVisible();
            };

            _form.HiddenToTray += delegate
            {
                try
                {
                    _tray.ShowBalloonTip(3000, "Виджет свёрнут в трей",
                        "Вернуть окно — значок в трее или ярлык «Виджет polza.ai».",
                        ToolTipIcon.Info);
                }
                catch { }
            };
            _form.DataUpdated += delegate { _tray.Text = SafeTip(_form.Summary); };

            _listener = new Thread(ListenLoop);
            _listener.IsBackground = true;
            _listener.Start();

            // Один тихий запрос через несколько секунд после старта: не мешает загрузке.
            var startupTimer = new System.Windows.Forms.Timer();
            startupTimer.Interval = 6000;
            startupTimer.Tick += delegate
            {
                startupTimer.Stop();
                startupTimer.Dispose();
                CheckForUpdates(true);
            };
            startupTimer.Start();
        }

        private async void CheckForUpdates(bool silent)
        {
            if (!Updater.Configured)
            {
                if (!silent)
                    MessageBox.Show("Проверка обновлений заработает после публикации репозитория.",
                        "Обновления", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            UpdateInfo info = await Updater.CheckAsync();
            if (info == null)
            {
                if (!silent)
                    MessageBox.Show("У вас последняя версия (" + Updater.Current + ").",
                        "Обновления", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _updateItem.Text = "Доступна версия " + info.Version;
            _updateItem.Tag = info.Url;
            _updateItem.Visible = true;

            if (!silent)
            {
                DialogResult answer = MessageBox.Show(
                    "Доступна версия " + info.Version + ". Открыть страницу загрузки?",
                    "Обновления", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (answer == DialogResult.Yes) OpenUpdatePage();
                return;
            }

            // Тихое уведомление — один раз на версию.
            if (Updater.LastNotified() != info.Version)
            {
                Updater.MarkNotified(info.Version);
                try
                {
                    _tray.ShowBalloonTip(6000, "Доступна версия " + info.Version,
                        "Нажмите, чтобы открыть страницу загрузки.", ToolTipIcon.Info);
                }
                catch { }
            }
        }

        private void OpenUpdatePage()
        {
            string url = _updateItem.Tag as string;
            if (string.IsNullOrEmpty(url)) return;
            try { System.Diagnostics.Process.Start(url); } catch { }
        }

        private static string SafeTip(string text)
        {
            if (string.IsNullOrEmpty(text)) return "polza.ai";
            return text.Length > 63 ? text.Substring(0, 63) : text;
        }

        private void ListenLoop()
        {
            while (!_stopping)
            {
                if (!_showEvent.WaitOne(400)) continue;
                try { _form.BeginInvoke((Action)(() => _form.ShowFromTray())); }
                catch { }
            }
        }

        private void ExitApp()
        {
            _stopping = true;
            try { _tray.Visible = false; _tray.Dispose(); } catch { }
            _form.ForceClose = true;
            _form.Close();
            ExitThread();
        }
    }
}
