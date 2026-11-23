using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PolzaWidget
{
    /// <summary>Карточка расходов: без рамки, без панели задач и без Alt+Tab.</summary>
    internal sealed class WidgetForm : Form
    {
        public const int CardWidth = 318;
        public const int CardHeight = 224;
        private const int Radius = 12;

        private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
        private static readonly Rectangle RefreshRect = new Rectangle(252, 3, 28, 24);
        private static readonly Rectangle CloseRect = new Rectangle(284, 3, 24, 24);

        private readonly AppSettings _settings;
        private readonly ApiClient _api;
        private readonly Timer _timer;

        private decimal _total;
        private long _prompt;
        private long _completion;
        private long _cached;
        private int _count;
        private decimal? _balance;
        private string _status = "загрузка…";
        private bool _fresh;
        private bool _busy;

        private int _hover;
        private bool _dragging;
        private Point _dragOffset;

        private readonly Font _fBrand = new Font("Segoe UI", 8.5f);
        private readonly Font _fAmount = new Font("Segoe UI", 22f, FontStyle.Bold);
        private readonly Font _fLabel = new Font("Segoe UI", 8f);
        private readonly Font _fValue = new Font("Segoe UI", 9.5f);
        private readonly Font _fIcon = new Font("Segoe MDL2 Assets", 9f);
        private readonly Font _fSmall = new Font("Segoe UI", 8f);

        public bool ForceClose;

        /// <summary>Карточку свернули в трей — контекст покажет подсказку.</summary>
        public event EventHandler HiddenToTray;

        /// <summary>Пришли новые цифры — обновить подсказку в трее.</summary>
        public event EventHandler DataUpdated;

        public WidgetForm(AppSettings settings)
        {
            _settings = settings;
            _api = new ApiClient(settings.apiKey);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(CardWidth, CardHeight);
            BackColor = Color.FromArgb(30, 30, 30);
            Text = "polza.ai — расходы";
            TopMost = settings.topmost;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            _timer = new Timer();
            _timer.Interval = Math.Max(10, settings.refreshSeconds) * 1000;
            _timer.Tick += delegate { RefreshNow(); };

            RestorePosition();
            UpdateRegion();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // WS_EX_TOOLWINDOW: окно не попадает в Alt+Tab и на панель задач.
                cp.ExStyle |= 0x00000080;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateRegion();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, Width, Height), Radius))
                Region = new Region(path);
        }

        private static GraphicsPath RoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // --- позиция ---------------------------------------------------------
        private void RestorePosition()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int left = area.Right - CardWidth - 20;
            int top = area.Top + 20;

            WidgetState saved = WidgetState.Load();
            if (saved != null)
            {
                // Позицию выбирает пользователь: берём её как есть, если окно вообще видно.
                var rect = new Rectangle(saved.x, saved.y, CardWidth, CardHeight);
                if (area.IntersectsWith(rect))
                {
                    left = saved.x;
                    top = saved.y;
                }
            }
            Location = new Point(left, top);
        }

        public void SavePosition()
        {
            new WidgetState { x = Left, y = Top }.Save();
        }

        // --- данные ----------------------------------------------------------
        public void StartAutoRefresh()
        {
            _timer.Start();
            RefreshNow();
        }

        public async void RefreshNow()
        {
            if (_busy) return;
            _busy = true;
            _status = "обновление…";
            Invalidate(new Rectangle(0, 186, CardWidth, 38));

            try
            {
                Snapshot snapshot = await _api.FetchAsync();
                _total = snapshot.Total;
                _prompt = snapshot.Prompt;
                _completion = snapshot.Completion;
                _cached = snapshot.Cached;
                _count = snapshot.Count;
                _balance = snapshot.Balance;
                _status = "обновлено " + snapshot.Updated.ToString("HH:mm", Ru);
                _fresh = true;
            }
            catch (Exception ex)
            {
                _status = "ошибка сети";
                _fresh = false;
                Log.Write("ошибка сети: " + Innermost(ex).Message);
            }
            finally
            {
                _busy = false;
                Invalidate();
                EventHandler handler = DataUpdated;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        private static Exception Innermost(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }

        public string Summary
        {
            get { return "polza.ai — " + _total.ToString("N2", Ru) + " ₽ за сегодня"; }
        }

        // --- отрисовка -------------------------------------------------------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
            {
                using (var fill = new SolidBrush(Color.FromArgb(30, 30, 30))) g.FillPath(fill, path);
                using (var border = new Pen(Color.FromArgb(51, 255, 255, 255))) g.DrawPath(border, path);
            }

            Color muted = Color.FromArgb(155, 155, 155);
            Color dim = Color.FromArgb(122, 122, 122);

            using (var dot = new SolidBrush(_fresh ? Color.FromArgb(76, 217, 100) : Color.FromArgb(255, 69, 58)))
                g.FillEllipse(dot, 14, 13, 8, 8);

            DrawText(g, "polza.ai", _fBrand, muted, new Rectangle(24, 4, 160, 24), StringAlignment.Near);
            DrawText(g, "\uE72C", _fIcon, _hover == 1 ? Color.White : muted, RefreshRect, StringAlignment.Center);
            DrawText(g, "\uE711", _fIcon, _hover == 2 ? Color.White : muted, CloseRect, StringAlignment.Center);

            DrawText(g, _total.ToString("N2", Ru) + " ₽", _fAmount, Color.White,
                     new Rectangle(14, 22, 290, 42), StringAlignment.Near);
            DrawText(g, "расход за сегодня", _fSmall, Color.FromArgb(138, 138, 138),
                     new Rectangle(14, 66, 290, 16), StringAlignment.Near);

            using (var line = new Pen(Color.FromArgb(42, 255, 255, 255))) g.DrawLine(line, 14, 90, 304, 90);

            DrawMetric(g, "вход", FormatTokens(_prompt), 96, 14, 150);
            DrawMetric(g, "выход", FormatTokens(_completion), 96, 162, 304);
            DrawMetric(g, "кэш", FormatTokens(_cached), 116, 14, 150);
            DrawMetric(g, "запросов", _count.ToString("N0", Ru), 116, 162, 304);

            string balance = _balance.HasValue
                ? "баланс " + _balance.Value.ToString("N2", Ru) + " ₽"
                : "баланс —";
            DrawText(g, balance, _fSmall, dim, new Rectangle(14, 192, 180, 18), StringAlignment.Near);
            DrawText(g, _status, _fSmall, dim, new Rectangle(180, 192, 124, 18), StringAlignment.Far);
        }

        private void DrawMetric(Graphics g, string label, string value, int y, int labelLeft, int valueRight)
        {
            DrawText(g, label, _fLabel, Color.FromArgb(122, 122, 122),
                     new Rectangle(labelLeft, y, 80, 16), StringAlignment.Near);
            DrawText(g, value, _fValue, Color.FromArgb(232, 232, 232),
                     new Rectangle(valueRight - 110, y, 110, 16), StringAlignment.Far);
        }

        private static void DrawText(Graphics g, string text, Font font, Color color, Rectangle rect, StringAlignment align)
        {
            using (var format = new StringFormat())
            {
                format.Alignment = align;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags = StringFormatFlags.NoWrap;
                format.Trimming = StringTrimming.EllipsisCharacter;
                using (var brush = new SolidBrush(color)) g.DrawString(text, font, brush, rect, format);
            }
        }

        private static string FormatTokens(long value)
        {
            if (value >= 1000000) return (value / 1000000.0).ToString("N1", Ru) + " млн";
            if (value >= 1000) return (value / 1000.0).ToString("N1", Ru) + " тыс";
            return value.ToString("N0", Ru);
        }

        // --- мышь ------------------------------------------------------------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            if (RefreshRect.Contains(e.Location)) { RefreshNow(); return; }
            if (CloseRect.Contains(e.Location)) { HideToTray(); return; }
            if (e.Clicks == 2)
            {
                try { System.Diagnostics.Process.Start("https://polza.ai/dashboard"); } catch { }
                return;
            }
            _dragging = true;
            _dragOffset = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hover = RefreshRect.Contains(e.Location) ? 1 : (CloseRect.Contains(e.Location) ? 2 : 0);
            if (hover != _hover)
            {
                _hover = hover;
                Cursor = hover != 0 ? Cursors.Hand : Cursors.SizeAll;
                Invalidate(new Rectangle(248, 0, 70, 30));
            }
            if (_dragging)
            {
                Point cursor = Cursor.Position;
                Location = new Point(cursor.X - _dragOffset.X, cursor.Y - _dragOffset.Y);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging)
            {
                _dragging = false;
                SavePosition();
            }
        }

        private void HideToTray()
        {
            Hide();
            EventHandler handler = HiddenToTray;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public void ShowFromTray()
        {
            if (!Visible) Show();
            TopMost = true;
            Activate();
            TopMost = _settings.topmost;
        }

        public void ToggleVisible()
        {
            if (Visible && ContainsFocus) Hide();
            else ShowFromTray();
        }

        public void SetTopmost(bool value)
        {
            TopMost = value;
            _settings.topmost = value;
            try { _settings.Save(); } catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!ForceClose)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            _timer.Stop();
            _timer.Dispose();
            base.OnFormClosing(e);
        }
    }
}
