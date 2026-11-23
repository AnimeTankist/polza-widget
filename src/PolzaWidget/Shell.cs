using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace PolzaWidget
{
    /// <summary>Ярлыки, автозапуск, иконка. Никакой записи в реестр — только файлы в профиле.</summary>
    internal static class Shell
    {
        public static string ExePath
        {
            get { return Application.ExecutablePath; }
        }

        public static string StartupLink
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "PolzaWidget.lnk"); }
        }

        public static string DesktopLink
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Виджет polza.ai.lnk"); }
        }

        public static void CreateShortcut(string path)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            object shell = Activator.CreateInstance(shellType);
            try
            {
                dynamic link = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
                    null, shell, new object[] { path });
                link.TargetPath = ExePath;
                link.WorkingDirectory = Path.GetDirectoryName(ExePath);
                link.Description = "Расходы polza.ai за сегодня";
                link.IconLocation = ExePath + ",0";
                link.Save();
            }
            finally
            {
                if (shell != null && System.Runtime.InteropServices.Marshal.IsComObject(shell))
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
        }

        public static bool IsAutoStart()
        {
            try { return File.Exists(StartupLink); } catch { return false; }
        }

        public static void SetAutoStart(bool enabled)
        {
            try
            {
                if (enabled) CreateShortcut(StartupLink);
                else if (File.Exists(StartupLink)) File.Delete(StartupLink);
            }
            catch { }
        }

        public static void Uninstall()
        {
            try { if (File.Exists(StartupLink)) File.Delete(StartupLink); } catch { }
            try { if (File.Exists(DesktopLink)) File.Delete(DesktopLink); } catch { }
            MessageBox.Show(
                "Ярлыки и автозапуск удалены.\r\n\r\nПапку с настройками можно удалить вручную:\r\n" + AppSettings.DataDir,
                "Виджет polza.ai", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static Icon TrayIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    using (var back = new SolidBrush(Color.FromArgb(30, 30, 30)))
                        g.FillEllipse(back, 0, 0, 31, 31);
                    using (var font = new Font("Segoe UI", 15f, FontStyle.Bold))
                    using (var white = new SolidBrush(Color.White))
                    using (var format = new StringFormat())
                    {
                        format.Alignment = StringAlignment.Center;
                        format.LineAlignment = StringAlignment.Center;
                        g.DrawString("₽", font, white, new RectangleF(0, 0, 32, 32), format);
                    }
                }
                return Icon.FromHandle(bitmap.GetHicon());
            }
        }
    }
}
