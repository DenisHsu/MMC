using FibonacciMVC.Models;
using Microsoft.Extensions.Caching.Memory;

namespace FibonacciMVC.Services;

/// <summary>
/// 整合 Yahoo Finance 價格、TWSE 三大法人、融資融券，
/// 計算綜合評分並產生買賣建議。
/// </summary>
public class StockAnalysisService : IStockAnalysisService
{
    private readonly IStockService   _priceService;
    private readonly ITwseDataService _twseService;
    private readonly IMemoryCache    _cache;
    private readonly ILogger<StockAnalysisService> _logger;

    // 篩選池：台股市值前30大或流動性高的個股
    private static readonly string[] WatchList =
    [
        "2330","2317","2454","2382","2308","2303","3711","2345","2886","2891",
        "2882","2881","1301","1303","2412","2002","2207","3008","2379","2395",
        "4938","6505","2357","2376","3034","2474","2408","2301","5871","2884"
    ];

    public StockAnalysisService(
        IStockService priceService,
        ITwseDataService twseService,
        IMemoryCache cache,
        ILogger<StockAnalysisService> logger)
    {
        _priceService = priceService;
        _twseService  = twseService;
        _cache        = cache;
        _logger       = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // 完整個股分析
    // ─────────────────────────────────────────────────────────────
    public async Task<StockViewModel> GetFullAnalysisAsync(string stockCode)
    {
        var (vmTask, instTask, marginTask) = (
            _priceService.GetStockDataAsync(stockCode),
            _twseService.GetInstitutionalHistoryAsync(stockCode),
            _twseService.GetMarginDataAsync(stockCode)
        );
        await Task.WhenAll(vmTask, instTask, marginTask);

        var vm = await vmTask;
        vm.InstitutionalHistory = await instTask;
        vm.Margin  = await marginTask;

        // instTask（TWT38U）解析後會快取中文名稱，優先使用
        var chineseName = _twseService.GetChineseName(stockCode);
        if (!string.IsNullOrEmpty(chineseName))
            vm.Name = chineseName;

        vm.Analysis = ComputeScore(vm);
        return vm;
    }

    // ─────────────────────────────────────────────────────────────
    // 首頁推薦5檔
    // ─────────────────────────────────────────────────────────────
    public async Task<HomeViewModel> GetTopRecommendationsAsync(int count = 5)
    {
        const string cacheKey = "home_recommendations";
        if (_cache.TryGetValue(cacheKey, out HomeViewModel? cached) && cached != null)
            return cached;

        var hvm = new HomeViewModel { LastUpdated = DateTime.Now };

        try
        {
            // 步驟 1：取全市場三大法人（一次 API 呼叫）
            var allInst = await _twseService.GetAllInstitutionalAsync();

            if (allInst.Count == 0)
            {
                hvm.ErrorMessage = "無法取得三大法人資料（可能尚未收盤或為假日），請稍後再試。";
                return hvm;
            }

            // 步驟 2：篩選觀察池，依籌碼初步評分
            var candidates = WatchList
                .Where(allInst.ContainsKey)
                .Select(code =>
                {
                    var s = allInst[code];
                    int score = 0;
                    if (s.ForeignNet > 0)        score += 25;
                    if (s.ForeignNet > 5_000_000) score += 10; // 大量外資買超
                    if (s.InvestTrustNet > 0)     score += 20;
                    if (s.TotalNet > 0)            score += 10;
                    return (code, score, s);
                })
                .Where(x => x.score >= 30)   // 至少有一項主力買超
                .OrderByDescending(x => x.score)
                .Take(12)
                .ToList();

            if (candidates.Count == 0)
            {
                hvm.ErrorMessage = "今日篩選池內無符合條件的個股（籌碼未明顯集中）。";
                return hvm;
            }

            // 步驟 3：平行取價格資料
            var priceTasks = candidates
                .Select(c => SafeFetchPrice(c.code))
                .ToArray();
            var prices = await Task.WhenAll(priceTasks);

            // 步驟 4：組合並計算完整評分
            var recs = new List<StockRecommendation>();
            for (int i = 0; i < candidates.Count; i++)
            {
                var priceVm = prices[i];
                if (priceVm == null || priceVm.HasError || !priceVm.HasResult) continue;

                var (code, _, instSummary) = candidates[i];
                var score = ComputeScoreFromSummary(priceVm, instSummary);

                var reasons = BuildReasonList(instSummary);
                var (entry, target, stopLoss) = GetEntryTarget(priceVm);

                // T86 呼叫後 cname 快取已填入，優先顯示中文名稱
                var recChineseName = _twseService.GetChineseName(code);

                recs.Add(new StockRecommendation
                {
                    Code = code,
                    Name = !string.IsNullOrEmpty(recChineseName) ? recChineseName
                           : (priceVm.Name ?? code),
                    CurrentPrice         = priceVm.CurrentPrice,
                    EntryPrice           = entry,
                    TargetPrice          = target,
                    StopLossPrice        = stopLoss,
                    Score                = score.Total,
                    RecommendationText   = score.RecommendationText,
                    RecommendationCssClass = score.RecommendationCssClass,
                    Reason               = string.Join("、", reasons),
                });
            }

            hvm.Recommendations = recs
                .OrderByDescending(r => r.Score)
                .Where(r => r.Score >= 50)
                .Take(count)
                .ToList();

            _cache.Set(cacheKey, hvm, TimeSpan.FromMinutes(30));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTopRecommendationsAsync failed");
            hvm.ErrorMessage = "系統發生錯誤，請稍後再試。";
        }

        return hvm;
    }

    // ─────────────────────────────────────────────────────────────
    // 評分邏輯
    // ─────────────────────────────────────────────────────────────

    private static AnalysisScore ComputeScore(StockViewModel vm)
    {
        var s = new AnalysisScore();

        // 1. 外資動向（0-30）
        if (vm.HasInstitutionalData)
        {
            var recent = vm.InstitutionalHistory.Take(3).ToList();
            int buyDays = recent.Count(r => r.ForeignNet > 0);
            s.ForeignScore = buyDays switch { 3 => 30, 2 => 20, 1 => 10, _ => 0 };
            s.ForeignReason = buyDays > 0
                ? $"近{recent.Count}日外資買超 {buyDays} 天（累計 {recent.Sum(r => r.ForeignNetK):N0} 張）"
                : $"近{recent.Count}日外資賣超";
        }
        else
        {
            s.ForeignReason = "無法取得三大法人資料（TWSE 今日可能尚未更新）";
        }

        // 2. 投信動向（0-20）
        if (vm.HasInstitutionalData)
        {
            var recent = vm.InstitutionalHistory.Take(3).ToList();
            int buyDays = recent.Count(r => r.InvestmentTrustNet > 0);
            s.InvestmentTrustScore = buyDays switch { 3 => 20, 2 => 15, 1 => 8, _ => 0 };
            s.InvestmentTrustReason = buyDays > 0 ? $"投信連買 {buyDays} 天" : "投信未持續買超";
        }

        // 3. 融資券動向（0-20）
        if (vm.HasMargin)
        {
            var m = vm.Margin!;
            if (m.MarginChange < 0)
            {
                s.MarginScore += 12;
                s.MarginReason = "融資餘額下降（去槓桿化，多方較乾淨）";
            }
            else
            {
                s.MarginReason = "融資餘額增加或持平";
            }
            if (m.ShortRatio > 20)
            {
                s.MarginScore += 8;
                s.MarginReason += "；融券比 > 20%，具軋空潛力";
            }
        }
        else
        {
            s.MarginReason = "無法取得融資券資料";
        }

        // 4. 費波那契位置（0-30）
        s.FibonacciScore  = FibScore(vm);
        s.FibonacciReason = FibReason(vm);

        // 計算買賣點
        var (entry, target, stopLoss) = GetEntryTarget(vm);
        s.EntryPrice    = entry;
        s.TargetPrice   = target;
        s.StopLossPrice = stopLoss;

        return s;
    }

    private static AnalysisScore ComputeScoreFromSummary(
        StockViewModel vm, InstitutionalSummary inst)
    {
        var s = new AnalysisScore();

        // 外資
        if (inst.ForeignNet > 5_000_000)        { s.ForeignScore = 30; s.ForeignReason = $"外資大買超 {inst.ForeignNet/1000:N0} 張"; }
        else if (inst.ForeignNet > 0)            { s.ForeignScore = 20; s.ForeignReason = $"外資買超 {inst.ForeignNet/1000:N0} 張"; }
        else                                     { s.ForeignScore =  0; s.ForeignReason = "外資賣超"; }

        // 投信
        if (inst.InvestTrustNet > 1_000_000)     { s.InvestmentTrustScore = 20; s.InvestmentTrustReason = $"投信大買超 {inst.InvestTrustNet/1000:N0} 張"; }
        else if (inst.InvestTrustNet > 0)        { s.InvestmentTrustScore = 12; s.InvestmentTrustReason = $"投信買超 {inst.InvestTrustNet/1000:N0} 張"; }
        else                                     { s.InvestmentTrustScore =  0; s.InvestmentTrustReason = "投信未買超"; }

        // 費波那契
        s.FibonacciScore  = FibScore(vm);
        s.FibonacciReason = FibReason(vm);

        var (entry, target, stopLoss) = GetEntryTarget(vm);
        s.EntryPrice    = entry;
        s.TargetPrice   = target;
        s.StopLossPrice = stopLoss;

        return s;
    }

    private static int FibScore(StockViewModel vm)
    {
        if (!vm.HasResult) return 0;
        return vm.PricePositionRatio switch
        {
            <= 0.236 => 30,
            <= 0.382 => 25,
            <= 0.500 => 15,
            <= 0.618 => 10,
            _        => 5,
        };
    }

    private static string FibReason(StockViewModel vm)
    {
        if (!vm.HasResult) return "無法取得價格資料";
        var pct = vm.PricePositionRatio * 100;
        return vm.PricePositionRatio switch
        {
            <= 0.236 => $"現價極靠近低點支撐（區間位置 {pct:F1}%）",
            <= 0.382 => $"現價位於黃金分割支撐區（{pct:F1}%）",
            <= 0.500 => $"現價在區間中段偏低（{pct:F1}%）",
            <= 0.618 => $"現價在區間中段（{pct:F1}%）",
            _        => $"現價靠近高點壓力區（{pct:F1}%）",
        };
    }

    private static (double Entry, double Target, double StopLoss) GetEntryTarget(StockViewModel vm)
    {
        if (!vm.HasResult || vm.FibonacciLevels.Count < 2)
            return (vm.CurrentPrice, vm.CurrentPrice * 1.05, vm.CurrentPrice * 0.97);

        var levels = vm.FibonacciLevels; // 由高到低排列
        double price = vm.CurrentPrice;

        for (int i = 0; i < levels.Count - 1; i++)
        {
            if (price <= levels[i].Price && price >= levels[i + 1].Price)
            {
                double entry    = levels[i + 1].Price;                         // 支撐
                double target   = levels[i].Price;                             // 壓力
                double stopLoss = i + 2 < levels.Count
                    ? levels[i + 2].Price
                    : entry * 0.97;
                return (entry, target, stopLoss);
            }
        }
        return (vm.DayLow, vm.DayHigh, vm.DayLow * 0.97);
    }

    private static List<string> BuildReasonList(InstitutionalSummary s)
    {
        var r = new List<string>();
        if (s.ForeignNet > 0)     r.Add($"外資 +{s.ForeignNet / 1000:N0} 張");
        if (s.InvestTrustNet > 0) r.Add($"投信 +{s.InvestTrustNet / 1000:N0} 張");
        if (s.TotalNet > 0)       r.Add($"三大合計 +{s.TotalNet / 1000:N0} 張");
        return r;
    }

    private async Task<StockViewModel?> SafeFetchPrice(string code)
    {
        try { return await _priceService.GetStockDataAsync(code); }
        catch { return null; }
    }
}
