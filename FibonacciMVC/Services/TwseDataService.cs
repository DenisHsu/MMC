using System.Text.Json;
using FibonacciMVC.Models;
using Microsoft.Extensions.Caching.Memory;

namespace FibonacciMVC.Services;

/// <summary>
/// 呼叫台灣證券交易所 (TWSE) Open API 取得三大法人與融資融券資料。
/// 伺服器端呼叫，無 CORS 問題。
/// </summary>
public class TwseDataService : ITwseDataService
{
    private readonly HttpClient   _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TwseDataService> _logger;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    public TwseDataService(HttpClient http, IMemoryCache cache, ILogger<TwseDataService> logger)
    {
        _http   = http;
        _cache  = cache;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════
    // 三大法人（單一股票，近N日）— TWSE TWT38U
    // ═══════════════════════════════════════════════════════════
    public async Task<List<InstitutionalRecord>> GetInstitutionalHistoryAsync(
        string stockCode, int days = 5)
    {
        var key = $"inst_{stockCode}_{Today}";
        if (_cache.TryGetValue(key, out List<InstitutionalRecord>? cached) && cached != null)
            return cached;

        var url = $"https://www.twse.com.tw/fund/TWT38U" +
                  $"?response=json&date={Today}&stockNo={stockCode}";
        try
        {
            var json = await _http.GetStringAsync(url);
            var doc  = JsonDocument.Parse(json);

            if (!TryGetDataArray(doc, out var rows)) return [];

            // TWT38U 欄位順序（0-based）：
            // [0]日期 [1]外資買 [2]外資賣 [3]外資差 [4]投信買 [5]投信賣 [6]投信差
            // [7]自營買 [8]自營賣 [9]自營差 [10]合計差
            var records = rows
                .Select(row =>
                {
                    var a = ToStringArray(row);
                    if (a.Length < 11) return null;
                    return new InstitutionalRecord
                    {
                        Date               = a[0],
                        ForeignNet         = ParseLong(a[3]),
                        InvestmentTrustNet = ParseLong(a[6]),
                        DealerNet          = ParseLong(a[9]),
                        TotalNet           = ParseLong(a[10]),
                    };
                })
                .Where(r => r != null)
                .Cast<InstitutionalRecord>()
                .TakeLast(days)
                .Reverse()   // 最新的排前面
                .ToList();

            _cache.Set(key, records, CacheTtl);
            return records;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetInstitutionalHistoryAsync failed: {Code}", stockCode);
            return [];
        }
    }

    // ═══════════════════════════════════════════════════════════
    // 融資融券（單一股票）— TWSE MI_MARGN (selectType=ALL，再過濾)
    // ═══════════════════════════════════════════════════════════
    public async Task<MarginRecord?> GetMarginDataAsync(string stockCode)
    {
        var key = $"margin_{stockCode}_{Today}";
        if (_cache.TryGetValue(key, out MarginRecord? cached)) return cached;

        // 嘗試今天及往前5個工作日
        foreach (var date in RecentWorkdays(5))
        {
            var url = $"https://www.twse.com.tw/exchangeReport/MI_MARGN" +
                      $"?response=json&date={date}&selectType=ALL";
            try
            {
                var json = await _http.GetStringAsync(url);
                var doc  = JsonDocument.Parse(json);

                if (!TryGetDataArray(doc, out var rows)) continue;

                // 動態找欄位索引
                var fields = GetFields(doc);
                int idxCode    = FindIdx(fields, "證券代號", 0);
                int idxMrgBal  = FindIdx(fields, "融資餘額",   5);
                int idxMrgPrev = FindIdx(fields, "融資前日",   6);
                int idxShtBal  = FindIdx(fields, "融券餘額",  11);
                int idxShtPrev = FindIdx(fields, "融券前日",  12);

                foreach (var row in rows)
                {
                    var a = ToStringArray(row);
                    if (a.Length <= Math.Max(idxShtPrev, idxCode)) continue;
                    if (!a[idxCode].Trim().Equals(stockCode, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var record = new MarginRecord
                    {
                        Date              = date,
                        MarginBalance     = ParseLong(a[idxMrgBal]),
                        MarginPrevBalance = ParseLong(a[idxMrgPrev]),
                        ShortBalance      = ParseLong(a[idxShtBal]),
                        ShortPrevBalance  = ParseLong(a[idxShtPrev]),
                    };
                    _cache.Set(key, record, CacheTtl);
                    return record;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetMarginDataAsync failed: {Code} {Date}", stockCode, date);
            }
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════
    // 全市場三大法人（首頁篩選用）— TWSE T86
    // ═══════════════════════════════════════════════════════════
    public async Task<Dictionary<string, InstitutionalSummary>> GetAllInstitutionalAsync()
    {
        var key = $"t86_{Today}";
        if (_cache.TryGetValue(key, out Dictionary<string, InstitutionalSummary>? cached)
            && cached != null)
            return cached;

        foreach (var date in RecentWorkdays(5))
        {
            var url = $"https://www.twse.com.tw/fund/T86" +
                      $"?response=json&date={date}&selectType=ALL";
            try
            {
                var json = await _http.GetStringAsync(url);
                var doc  = JsonDocument.Parse(json);

                if (!TryGetDataArray(doc, out var rows) || rows.Count == 0) continue;

                // T86 欄位（0-based）：
                // [0]代號 [1]名稱 [2]外買 [3]外賣 [4]外差  [5]投買 [6]投賣 [7]投差
                // [8]自行買 [9]自行賣 [10]自行差 [11]避險買 [12]避險賣 [13]避險差 [14]合計差
                var result = new Dictionary<string, InstitutionalSummary>(capacity: rows.Count);
                foreach (var row in rows)
                {
                    var a = ToStringArray(row);
                    if (a.Length < 15) continue;
                    result[a[0].Trim()] = new InstitutionalSummary(
                        ForeignNet:     ParseLong(a[4]),
                        InvestTrustNet: ParseLong(a[7]),
                        TotalNet:       ParseLong(a[14])
                    );
                }

                if (result.Count > 0)
                {
                    _cache.Set(key, result, CacheTtl);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAllInstitutionalAsync failed: {Date}", date);
            }
        }
        return [];
    }

    // ═══════════════════════════════════════════════════════════
    // 輔助方法
    // ═══════════════════════════════════════════════════════════

    private static string Today => DateTime.Now.ToString("yyyyMMdd");

    private static IEnumerable<string> RecentWorkdays(int count)
    {
        int found = 0;
        var d = DateTime.Today;
        while (found < count)
        {
            if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
            {
                yield return d.ToString("yyyyMMdd");
                found++;
            }
            d = d.AddDays(-1);
        }
    }

    private static bool TryGetDataArray(JsonDocument doc, out List<JsonElement> rows)
    {
        rows = [];
        if (!doc.RootElement.TryGetProperty("data", out var dataEl) ||
            dataEl.ValueKind != JsonValueKind.Array)
            return false;
        rows = dataEl.EnumerateArray().ToList();
        return rows.Count > 0;
    }

    private static string[] GetFields(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Array
            ? f.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
            : [];

    private static int FindIdx(string[] fields, string keyword, int fallback)
    {
        for (int i = 0; i < fields.Length; i++)
            if (fields[i].Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return i;
        return fallback;
    }

    private static string[] ToStringArray(JsonElement row) =>
        row.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();

    private static long ParseLong(string s)
    {
        var clean = s.Replace(",", "").Replace("－", "-").Replace("—", "0").Trim();
        return long.TryParse(clean, out var v) ? v : 0;
    }
}
