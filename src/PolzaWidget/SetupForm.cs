using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace PolzaWidget
{
    /// <summary>Первый запуск: спросить ключ и разрешения явно, без скрытых автозапусков.</summary>
    internal sealed class SetupForm : Form
    {
        private readonly TextBox _key = new TextBox();
        private readonly CheckBox _autoStart = new CheckBox();
        private readonly CheckBox _shortcut = new CheckBox();
        private readonly Label _status = new Label();

        public AppSettings Settings { get; private set; }

        public SetupForm()
        {
            Text = "Виджет расходов polza.ai";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(420, 254);
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f);

            var title = new Label();
            title.Text = "Нужен API-ключ polza.ai";
            title.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            title.ForeColor = Color.White;
            title.Location = new Point(18, 14);
            title.AutoSize = true;

            var hint = new Label();
            hint.Text = "Ключ начинается с pza_. Взять можно в Chatbox: Настройки → Модели →\r\nпровайдер Polza.ai, либо на polza.ai/dashboard/api-keys.";
            hint.ForeColor = Color.FromArgb(170, 170, 170);
            hint.Location = new Point(18, 44);
            hint.Size = new Size(384, 40);

            _key.Location = new Point(18, 90);
            _key.Size = new Size(384, 24);
            _key.BackColor = Color.FromArgb(45, 45, 45);
            _key.ForeColor = Color.White;
            _key.BorderStyle = BorderStyle.FixedSingle;

            var find = new Button();
            find.Text = "Найти в Chatbox";
            find.Location = new Point(18, 122);
            find.Size = new Size(150, 28);
            find.FlatStyle = FlatStyle.Flat;
            find.BackColor = Color.FromArgb(55, 55, 55);
            find.ForeColor = Color.White;
            find.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
            find.Click += delegate { FindInChatbox(); };

            _status.Text = "";
            _status.Location = new Point(176, 128);
            _status.Size = new Size(226, 20);
            _status.ForeColor = Color.FromArgb(150, 150, 150);

            _autoStart.Text = "Запускать при входе в систему";
            _autoStart.Checked = true;
            _autoStart.Location = new Point(18, 158);
            _autoStart.Size = new Size(320, 22);
            _autoStart.ForeColor = Color.White;

            _shortcut.Text = "Ярлык на рабочем столе";
            _shortcut.Checked = true;
            _shortcut.Location = new Point(18, 182);
            _shortcut.Size = new Size(320, 22);
            _shortcut.ForeColor = Color.White;

            var ok = new Button();
            ok.Text = "Готово";
            ok.DialogResult = DialogResult.OK;
            ok.Location = new Point(216, 214);
            ok.Size = new Size(90, 28);
            ok.FlatStyle = FlatStyle.Flat;
            ok.BackColor = Color.FromArgb(76, 217, 100);
            ok.ForeColor = Color.Black;

            var cancel = new Button();
            cancel.Text = "Отмена";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(312, 214);
            cancel.Size = new Size(90, 28);
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.BackColor = Color.FromArgb(55, 55, 55);
            cancel.ForeColor = Color.White;

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_key);
            Controls.Add(find);
            Controls.Add(_status);
            Controls.Add(_autoStart);
            Controls.Add(_shortcut);
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
            FormClosing += OnClosing;
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK) return;

            string key = _key.Text.Trim();
            if (!Regex.IsMatch(key, "^pza_[A-Za-z0-9_\\-]{10,}$"))
            {
                MessageBox.Show(this, "Ключ должен начинаться с pza_ и содержать не меньше 10 символов после него.",
                    "Проверьте ключ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                DialogResult = DialogResult.None;
                return;
            }

            Settings = new AppSettings();
            Settings.apiKey = key;
            Settings.refreshSeconds = 30;
            Settings.topmost = false;
            Settings.Save();

            if (_autoStart.Checked) Shell.SetAutoStart(true);
            if (_shortcut.Checked) Shell.CreateShortcut(Shell.DesktopLink);
        }

        private void FindInChatbox()
        {
            string key = ChatboxKey.Find();
            if (key != null)
            {
                _key.Text = key;
                _status.Text = "ключ найден";
                _status.ForeColor = Color.FromArgb(76, 217, 100);
            }
            else
            {
                _status.Text = "не найден — вставьте вручную";
                _status.ForeColor = Color.FromArgb(255, 159, 10);
            }
        }
    }

    /// <summary>Ищет ключ pza_… в настройках Chatbox.</summary>
    internal static class ChatboxKey
    {
        private static readonly Regex Pattern = new Regex("pza_[A-Za-z0-9]{20,}", RegexOptions.Compiled);

        public static string Find()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            string[] known =
            {
                Path.Combine(appData, "xyz.chatboxapp.app", "config.json"),
                Path.Combine(appData, "xyz.chatboxapp.ce", "config.json"),
                Path.Combine(appData, "Chatbox", "config.json")
            };

            foreach (string path in known)
            {
                string found = Match(path);
                if (found != null) return found;
            }

            // Запасной путь: два уровня вглубь по %APPDATA%.
            try
            {
                foreach (string dir in Directory.GetDirectories(appData))
                {
                    string direct = Match(Path.Combine(dir, "config.json"));
                    if (direct != null) return direct;

                    foreach (string sub in Directory.GetDirectories(dir))
                    {
                        string nested = Match(Path.Combine(sub, "config.json"));
                        if (nested != null) return nested;
                    }
                }
            }
            catch { }

            return null;
        }

        private static string Match(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > 5 * 1024 * 1024) return null;
                string text = File.ReadAllText(path, Encoding.UTF8);
                Match m = Pattern.Match(text);
                return m.Success ? m.Value : null;
            }
            catch { return null; }
        }
    }
}
