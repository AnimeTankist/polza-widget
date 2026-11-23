using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PolzaWidget
{
    public sealed class HistoryPage
    {
        public List<Generation> items;
        public PageMeta meta;
    }

    public sealed class PageMeta
    {
        public int? totalPages;
    }

    public sealed class Generation
    {
        public string id;
        public object cost;
        public string status;
        public TokenUsage usage;
    }

    public sealed class TokenUsage
    {
        public object prompt_tokens;
        public object completion_tokens;
        public TokenDetails prompt_tokens_details;
        public TokenDetails completion_tokens_details;
    }

    public sealed class TokenDetails
    {
        public object cached_tokens;
        public object reasoning_tokens;
    }

    public sealed class BalanceResponse
    {
        public object available;
    }

    /// <summary>Итоги за текущие сутки плюс баланс.</summary>
    internal sealed class Snapshot
    {
        public decimal Total;
        public long Prompt;
        public long Completion;
        public long Cached;
        public int Count;
        public decimal? Balance;
        public DateTime Updated;
    }

    internal sealed class ApiClient
    {
        private sealed class Entry
        {
            public decimal Cost;
            public long Prompt;
            public long Completion;
            public long Cached;
            public string Status;
        }

        private readonly HttpClient _http;
        private readonly Dictionary<string, Entry> _cache = new Dictionary<string, Entry>();
        private DateTime _cacheDate = DateTime.MinValue;

        public ApiClient(string apiKey)
        {
            _http = new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(30);
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("PolzaWidget/1.0");
        }

        public async Task<Snapshot> FetchAsync()
        {
            DateTime now = DateTime.Now;
            if (_cacheDate != now.Date)
            {
                _cache.Clear();
                _cacheDate = now.Date;
            }

            // API понимает только UTC с суффиксом Z: границы локальных суток переводим в UTC.
            string from = now.Date.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            string to = now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 8 * 1024 * 1024;

            int page = 1;
            int totalPages = 1;

            while (page <= 20)
            {
                string url = string.Format(CultureInfo.InvariantCulture,
                    "https://polza.ai/api/v1/history/generations?dateFrom={0}&dateTo={1}&limit=100&page={2}",
                    from, to, page);

                string json = await _http.GetStringAsync(url).ConfigureAwait(false);
                HistoryPage response = serializer.Deserialize<HistoryPage>(json);
                if (response == null || response.items == null || response.items.Count == 0) break;

                if (response.meta != null && response.meta.totalPages.HasValue && response.meta.totalPages.Value > 0)
                    totalPages = response.meta.totalPages.Value;

                bool allKnown = true;
                foreach (Generation item in response.items)
                {
                    if (item == null || string.IsNullOrEmpty(item.id)) continue;
                    if (!_cache.ContainsKey(item.id)) allKnown = false;

                    var entry = new Entry();
                    entry.Cost = Json.ToDecimal(item.cost);
                    entry.Status = item.status ?? "";
                    if (item.usage != null)
                    {
                        entry.Prompt = Json.ToLong(item.usage.prompt_tokens);
                        entry.Completion = Json.ToLong(item.usage.completion_tokens);
                        if (item.usage.prompt_tokens_details != null)
                            entry.Cached = Json.ToLong(item.usage.prompt_tokens_details.cached_tokens);
                    }
                    // Перезаписываем всегда: пока генерация идёт, стоимость и токены растут.
                    _cache[item.id] = entry;
                }

                if (allKnown)
                {
                    bool pending = false;
                    foreach (Entry value in _cache.Values)
                    {
                        if (!string.Equals(value.Status, "completed", StringComparison.OrdinalIgnoreCase))
                        {
                            pending = true;
                            break;
                        }
                    }
                    if (!pending) break;
                }

                if (page >= totalPages) break;
                page++;
            }

            decimal total = 0m;
            long prompt = 0L, completion = 0L, cached = 0L;
            foreach (Entry value in _cache.Values)
            {
                total += value.Cost;
                prompt += value.Prompt;
                completion += value.Completion;
                cached += value.Cached;
            }

            decimal? balance = null;
            try
            {
                string balanceJson = await _http.GetStringAsync("https://polza.ai/api/v2/balance").ConfigureAwait(false);
                BalanceResponse parsed = serializer.Deserialize<BalanceResponse>(balanceJson);
                if (parsed != null && parsed.available != null) balance = Json.ToDecimal(parsed.available);
            }
            catch { }

            var snapshot = new Snapshot();
            snapshot.Total = total;
            snapshot.Prompt = prompt;
            snapshot.Completion = completion;
            snapshot.Cached = cached;
            snapshot.Count = _cache.Count;
            snapshot.Balance = balance;
            snapshot.Updated = DateTime.Now;
            return snapshot;
        }
    }
}
