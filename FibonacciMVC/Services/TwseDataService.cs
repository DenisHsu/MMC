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
    // 策略：TWT38U 為月份資料；若本月筆數不足（月初、假日後），
    //       自動往前補抓上一個月，確保有足夠的前交易日資料。
    // ═══════════════════════════════════════════════════════════
    public async Task<List<InstitutionalRecord>> GetInstitutionalHistoryAsync(
        string stockCode, int days = 5)
    {
        var key = $"inst_{stockCode}_{Today}";
        if (_cache.TryGetValue(key, out List<InstitutionalRecord>? cached) && cached != null)
            return cached;

        // TWT38U 欄位順序（0-based）：
        // [0]日期 [1]外資買 [2]外資賣 [3]外資差 [4]投信買 [5]投信賣 [6]投信差
        // [7]自營買 [8]自營賣 [9]自營差 [10]合計差
        static InstitutionalRecord? ParseRow(JsonElement row)
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
        }

        // 依序嘗試：本月 → 上個月（月初資料不足時補充）
        var allRecords = new List<InstitutionalRecord>();
        var monthOffsets = new[] { 0, -1 };   // 0 = 本月, -1 = 上個月

        foreach (var offset in monthOffsets)
        {
            var refDate = DateTime.Today.AddMonths(offset).ToString("yyyyMMdd");
            var url = $"https://www.twse.com.tw/fund/TWT38U" +
                      $"?response=json&date={refDate}&stockNo={stockCode}";
            try
            {
                var json = await _http.GetStringAsync(url);
                var doc  = JsonDocument.Parse(json);

                if (!TryGetDataArray(doc, out var rows))
                {
                    // 本月尚無資料（例如月初未更新），繼續嘗試上個月
                    _logger.LogWarning("TWT38U 無資料：{Code} refDate={Date}", stockCode, refDate);
                    continue;
                }

                // 從 title 解析中文名稱並快取（取到就好，無須重複）
                if (!_cache.TryGetValue($"cname_{stockCode.ToUpper()}", out string? _) &&
                    doc.RootElement.TryGetProperty("title", out var titleEl))
                {
                    var chineseName = ExtractNameFromTitle(titleEl.GetString(), stockCode);
                    if (!string.IsNullOrEmpty(chineseName))
                        _cache.Set($"cname_{stockCode.ToUpper()}", chineseName, TimeSpan.FromHours(24));
                }

                var monthRecords = rows
                    .Select(ParseRow)
                    .Where(r => r != null)
                    .Cast<InstitutionalRecord>()
                    .ToList();

                // 上個月資料插在最前面，保持日期升序
                allRecords.InsertRange(0, monthRecords);

                if (allRecords.Count >= days) break;   // 資料已足夠
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetInstitutionalHistoryAsync failed: {Code} {Date}",
                    stockCode, refDate);
            }
        }

        if (allRecords.Count == 0) return [];

        // TakeLast 取最新 N 筆，Reverse 讓最新的排前面
        var result = allRecords.TakeLast(days).Reverse().ToList();
        _cache.Set(key, result, CacheTtl);
        return result;
    }

    // ═══════════════════════════════════════════════════════════
    // 融資融券（單一股票）— TWSE MI_MARGN (selectType=ALL，再過濾)
    // ═══════════════════════════════════════════════════════════
    public async Task<MarginRecord?> GetMarginDataAsync(string stockCode)
    {
        var key = $"margin_{stockCode}_{Today}";
        if (_cache.TryGetValue(key, out MarginRecord? cached)) return cached;

        // 嘗試今天及往前10個工作日（涵蓋連假後第一個交易日仍能取到前一交易日資料）
        foreach (var date in RecentWorkdays(10))
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
                var result    = new Dictionary<string, InstitutionalSummary>(capacity: rows.Count);
                // 同時建立 中文名稱 → 代號 的反向字典，供中文搜尋使用
                var nameToCode = new Dictionary<string, string>(
                    rows.Count, StringComparer.OrdinalIgnoreCase);

                foreach (var row in rows)
                {
                    var a = ToStringArray(row);
                    if (a.Length < 15) continue;
                    var code   = a[0].Trim();
                    var cnName = a[1].Trim();

                    result[code] = new InstitutionalSummary(
                        ForeignNet:     ParseLong(a[4]),
                        InvestTrustNet: ParseLong(a[7]),
                        TotalNet:       ParseLong(a[14])
                    );

                    if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(cnName))
                    {
                        _cache.Set($"cname_{code}", cnName, TimeSpan.FromHours(24));
                        nameToCode[cnName] = code;   // 反向索引
                    }
                }

                if (nameToCode.Count > 0)
                    _cache.Set("name_to_code", nameToCode, TimeSpan.FromHours(24));

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

    // ═══════════════════════════════════════════════════════════
    // 代號 / 名稱解析
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 將使用者輸入解析為股票代號：
    ///   - 純數字 → 直接使用
    ///   - 中文名稱 → 查 T86 反向字典（完全比對 → 前綴比對 → 包含比對）
    ///   - 找不到 → 回傳 null
    /// </summary>
    public async Task<string?> ResolveStockCodeAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        var q = query.Trim();

        // 純數字 → 直接當股票代號
        if (q.All(char.IsAsciiDigit)) return q;

        // 嘗試從快取取反向字典
        if (!_cache.TryGetValue("name_to_code", out Dictionary<string, string>? nameToCode)
            || nameToCode == null)
        {
            // 快取尚未建立，呼叫 T86 API 以填充（同時建立反向字典）
            await GetAllInstitutionalAsync();
            _cache.TryGetValue("name_to_code", out nameToCode);
        }

        if (nameToCode == null || nameToCode.Count == 0) return null;

        // 1. 完全比對
        if (nameToCode.TryGetValue(q, out var exact)) return exact;

        // 2. 前綴比對（輸入文字在名稱開頭）
        var prefix = nameToCode
            .Where(kv => kv.Key.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => kv.Key.Length)     // 優先較短（較精確）的名稱
            .Select(kv => kv.Value)
            .FirstOrDefault();
        if (prefix != null) return prefix;

        // 3. 包含比對（至少 2 個字元，避免單字模糊太廣）
        if (q.Length >= 2)
        {
            var contains = nameToCode
                .Where(kv => kv.Key.Contains(q, StringComparison.OrdinalIgnoreCase))
                .OrderBy(kv => kv.Key.Length)
                .Select(kv => kv.Value)
                .FirstOrDefault();
            if (contains != null) return contains;
        }

        return null;   // 找不到
    }

    // ═══════════════════════════════════════════════════════════
    // 中文名稱快取
    // ═══════════════════════════════════════════════════════════
    public string? GetChineseName(string stockCode)
    {
        _cache.TryGetValue($"cname_{stockCode.ToUpper()}", out string? name);
        return name;
    }

    /// <summary>從 TWT38U title 解析中文名稱，格式：...年...月 CODE 中文名稱 各法人...</summary>
    private static string? ExtractNameFromTitle(string? title, string stockCode)
    {
        if (string.IsNullOrEmpty(title)) return null;
        // 以半形/全形空白切割
        var parts = title.Split([' ', '　', '\t'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (string.Equals(parts[i].Trim(), stockCode.Trim(), StringComparison.OrdinalIgnoreCase))
                return parts[i + 1].Trim();
        }
        return null;
    }

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
