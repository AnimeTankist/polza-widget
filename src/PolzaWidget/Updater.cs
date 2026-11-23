using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PolzaWidget
{
    public sealed class ReleaseInfo
    {
        public string tag_name;
        public string html_url;
        public string body;
    }

    internal sealed class UpdateInfo
    {
        public string Version;
        public string Url;
    }

    /// <summary>
    /// Проверка новых версий через GitHub Releases.
    /// Приложение ничего не скачивает и не подменяет само себя: оно только сообщает
    /// о версии и открывает страницу релиза в браузере.
    /// </summary>
    internal static class Updater
    {
        // Заполняется после публикации репозитория.
        public const string Owner = "AnimeTankist";
        public const string Repo = "polza-widget";

        public static bool Configured
        {
            get { return Owner != "OWNER"; }
        }

        public static string Current
        {
            get
            {
                Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                int build = version.Build > 0 ? version.Build : 0;
                return version.Major + "." + version.Minor + "." + build;
            }
        }

        public static async Task<UpdateInfo> CheckAsync()
        {
            if (!Configured) return null;
            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(15);
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("PolzaWidget/" + Current);
                    http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                    string url = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";
                    string json = await http.GetStringAsync(url).ConfigureAwait(false);

                    var release = new JavaScriptSerializer().Deserialize<ReleaseInfo>(json);
                    if (release == null || string.IsNullOrEmpty(release.tag_name)) return null;
                    if (Compare(release.tag_name, Current) <= 0) return null;

                    var info = new UpdateInfo();
                    info.Version = release.tag_name.TrimStart('v', 'V');
                    info.Url = release.html_url;
                    return info;
                }
            }
            catch { return null; }
        }

        /// <summary>Версии вида 1.2.3 / v1.2.3; старшие части важнее младших.</summary>
        public static int Compare(string left, string right)
        {
            int[] a = Parse(left);
            int[] b = Parse(right);
            for (int i = 0; i < 3; i++)
            {
                int x = i < a.Length ? a[i] : 0;
                int y = i < b.Length ? b[i] : 0;
                if (x != y) return x < y ? -1 : 1;
            }
            return 0;
        }

        private static int[] Parse(string value)
        {
            value = (value ?? "").Trim().TrimStart('v', 'V');
            string[] parts = value.Split('.', '-', '+');
            var numbers = new List<int>();
            foreach (string part in parts)
            {
                int number;
                if (int.TryParse(part, out number)) numbers.Add(number);
                else break;
            }
            return numbers.ToArray();
        }

        // --- память о показанном уведомлении ---------------------------------
        private static string StampPath
        {
            get { return Path.Combine(AppSettings.DataDir, "update.txt"); }
        }

        public static string LastNotified()
        {
            try { return File.Exists(StampPath) ? File.ReadAllText(StampPath).Trim() : ""; }
            catch { return ""; }
        }

        public static void MarkNotified(string version)
        {
            try { File.WriteAllText(StampPath, version ?? ""); }
            catch { }
        }
    }
}
