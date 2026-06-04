using System.Text.Json;
using System.Text.Json.Serialization;
using FibonacciMVC.Models;

namespace FibonacciMVC.Services;

/// <summary>
/// 透過 Yahoo Finance v8 Chart API 取得台股上市 (.TW) / 上櫃 (.TWO) 即時行情。
/// </summary>
public class YahooFinanceService : IStockService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<YahooFinanceService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // 費波那契分割比例（由高到低）
    private static readonly (string Label, double Ratio)[] Levels =
    [
        ("100%（高點）", 1.000),
        ("78.6%",        0.786),
        ("61.8%",        0.618),
        ("50%",          0.500),
        ("38.2%",        0.382),
        ("23.6%",        0.236),
        ("0%（低點）",   0.000),
    ];

    public YahooFinanceService(HttpClient httpClient, ILogger<YahooFinanceService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    public async Task<StockViewModel> GetStockDataAsync(string stockCode)
    {
        var vm = new StockViewModel { Code = stockCode.ToUpper() };

        // 先試上市 (.TW)，找不到再試上櫃 (.TWO)
        string[] suffixes = [".TW", ".TWO"];
        YahooMeta? meta = null;
        string? usedSymbol = null;

        foreach (var suffix in suffixes)
        {
            usedSymbol = stockCode.ToUpper() + suffix;
            meta = await FetchMetaAsync(usedSymbol);
            if (meta is not null) break;
        }

        if (meta is null)
        {
            vm.ErrorMessage = $"查無股票「{stockCode}」，請確認代號是否正確（支援台股上市 / 上櫃）";
            return vm;
        }

        vm.Symbol = usedSymbol;
        vm.Name = !string.IsNullOrWhiteSpace(meta.ShortName) ? meta.ShortName : stockCode;
        vm.CurrentPrice = meta.RegularMarketPrice;
        vm.DayHigh = meta.RegularMarketDayHigh;
        vm.DayLow = meta.RegularMarketDayLow;
        vm.QueryTime = DateTime.Now;

        if (vm.DayHigh <= vm.DayLow)
        {
            vm.ErrorMessage = "今日高低點相同，無法計算費波那契區間（可能尚未開盤或停牌）";
            return vm;
        }

        BuildFibonacciLevels(vm);
        return vm;
    }

    // ─────────────────────────────────────────────────────────────
    private async Task<YahooMeta?> FetchMetaAsync(string symbol)
    {
        try
        {
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}" +
                      "?interval=1d&range=1d&includePrePost=false";

            var resp = await _httpClient.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Yahoo Finance HTTP {Status} for {Symbol}", (int)resp.StatusCode, symbol);
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync();
            var root = JsonSerializer.Deserialize<YahooRoot>(json, JsonOpts);

            var result = root?.Chart?.Result?.FirstOrDefault();
            if (result?.Meta is null) return null;

            // 確認是有效的股票（High/Low 不為零）
            var m = result.Meta;
            if (m.RegularMarketDayHigh == 0 && m.RegularMarketDayLow == 0) return null;

            return m;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FetchMetaAsync failed for {Symbol}", symbol);
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────
    private static void BuildFibonacciLevels(StockViewModel vm)
    {
        var range = vm.DayHigh - vm.DayLow;
        var price = vm.CurrentPrice;

        vm.FibonacciLevels = Levels.Select(l => new FibonacciLevel
        {
            Label          = l.Label,
            Ratio          = l.Ratio,
            Price          = vm.DayLow + range * l.Ratio,
            DiffFromCurrent = (vm.DayLow + range * l.Ratio) - price,
            DiffPercent    = ((vm.DayLow + range * l.Ratio) - price) / price * 100,
        }).ToList();

        // 標記現價所在區間（上下兩條）
        for (int i = 0; i < vm.FibonacciLevels.Count - 1; i++)
        {
            if (price <= vm.FibonacciLevels[i].Price &&
                price >= vm.FibonacciLevels[i + 1].Price)
            {
                vm.FibonacciLevels[i].IsInZone     = true;
                vm.FibonacciLevels[i + 1].IsInZone = true;
                break;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Yahoo Finance 回應 DTOs（私有，僅供反序列化使用）
    // ─────────────────────────────────────────────────────────────

    private sealed class YahooRoot
    {
        public YahooChart? Chart { get; set; }
    }

    private sealed class YahooChart
    {
        public List<YahooResult>? Result { get; set; }
    }

    private sealed class YahooResult
    {
        public YahooMeta? Meta { get; set; }
    }

    private sealed class YahooMeta
    {
        public string? ShortName { get; set; }
        public double RegularMarketPrice { get; set; }
        public double RegularMarketDayHigh { get; set; }
        public double RegularMarketDayLow { get; set; }
        public string? Currency { get; set; }
        public string? Symbol { get; set; }
    }
}
