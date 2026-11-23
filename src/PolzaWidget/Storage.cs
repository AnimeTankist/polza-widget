using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace PolzaWidget
{
    /// <summary>Настройки виджета. Живут в %LOCALAPPDATA%\PolzaWidget\config.json.</summary>
    internal sealed class AppSettings
    {
        public string apiKey = "";
        public int refreshSeconds = 30;
        public bool topmost = false;

        public static string DataDir
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PolzaWidget");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string ConfigPath { get { return Path.Combine(DataDir, "config.json"); } }
        public static string StatePath { get { return Path.Combine(DataDir, "state.json"); } }
        public static string LogPath { get { return Path.Combine(DataDir, "widget.log"); } }

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return null;
                string text = File.ReadAllText(ConfigPath, Encoding.UTF8);
                var s = new JavaScriptSerializer().Deserialize<AppSettings>(text);
                if (s == null) return null;
                if (s.refreshSeconds < 10) s.refreshSeconds = 10;
                return s;
            }
            catch { return null; }
        }

        public void Save()
        {
            string json = "{\r\n" +
                          "  \"apiKey\": \"" + Escape(apiKey) + "\",\r\n" +
                          "  \"refreshSeconds\": " + refreshSeconds.ToString(CultureInfo.InvariantCulture) + ",\r\n" +
                          "  \"topmost\": " + (topmost ? "true" : "false") + "\r\n" +
                          "}";
            File.WriteAllText(ConfigPath, json, new UTF8Encoding(true));
        }

        private static string Escape(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }

    /// <summary>Последняя позиция карточки. Точку выбирает пользователь, поэтому храним как есть.</summary>
    internal sealed class WidgetState
    {
        public int x;
        public int y;

        public static WidgetState Load()
        {
            try
            {
                if (!File.Exists(AppSettings.StatePath)) return null;
                return new JavaScriptSerializer().Deserialize<WidgetState>(
                    File.ReadAllText(AppSettings.StatePath, Encoding.UTF8));
            }
            catch { return null; }
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(AppSettings.StatePath,
                    "{\"x\":" + x + ",\"y\":" + y + "}", new UTF8Encoding(false));
            }
            catch { }
        }
    }

    /// <summary>Журнал: только старт и ошибки, с ротацией.</summary>
    internal static class Log
    {
        private const long Limit = 64 * 1024;

        public static void RotateIfNeeded()
        {
            try
            {
                var fi = new FileInfo(AppSettings.LogPath);
                if (!fi.Exists || fi.Length <= Limit) return;
                string[] lines = File.ReadAllLines(AppSettings.LogPath, Encoding.UTF8);
                int from = Math.Max(0, lines.Length - 200);
                var keep = new List<string>();
                for (int i = from; i < lines.Length; i++) keep.Add(lines[i]);
                File.WriteAllLines(AppSettings.LogPath, keep, new UTF8Encoding(true));
            }
            catch { }
        }

        public static void Write(string message)
        {
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message;
                File.AppendAllText(AppSettings.LogPath, line + "\r\n", new UTF8Encoding(true));
            }
            catch { }
        }
    }

    internal static class Json
    {
        public static decimal ToDecimal(object value)
        {
            if (value == null) return 0m;
            if (value is decimal) return (decimal)value;
            if (value is double) return (decimal)(double)value;
            if (value is int) return (int)value;
            if (value is long) return (long)value;
            string s = value as string;
            decimal result;
            if (s != null && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out result)) return result;
            return 0m;
        }

        public static long ToLong(object value)
        {
            if (value == null) return 0L;
            if (value is long) return (long)value;
            if (value is int) return (int)value;
            if (value is decimal) return (long)(decimal)value;
            if (value is double) return (long)(double)value;
            string s = value as string;
            long result;
            if (s != null && long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out result)) return result;
            return 0L;
        }
    }
}
