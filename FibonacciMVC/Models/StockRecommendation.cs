namespace FibonacciMVC.Models;

/// <summary>首頁推薦個股（精簡版）</summary>
public class StockRecommendation
{
    public string Code    { get; set; } = string.Empty;
    public string Name    { get; set; } = string.Empty;
    public double CurrentPrice  { get; set; }
    public double EntryPrice    { get; set; }
    public double TargetPrice   { get; set; }
    public double StopLossPrice { get; set; }
    public int    Score         { get; set; }

    public string RecommendationText     { get; set; } = string.Empty;
    public string RecommendationCssClass { get; set; } = string.Empty;

    /// <summary>主要買進理由（摘要）</summary>
    public string Reason { get; set; } = string.Empty;

    // ── 計算欄位 ──────────────────────────────────────────────────

    /// <summary>至目標價漲幅</summary>
    public double UpSidePct =>
        EntryPrice > 0 ? (TargetPrice - EntryPrice) / EntryPrice * 100 : 0;

    /// <summary>至停損跌幅（負值）</summary>
    public double DownSidePct =>
        EntryPrice > 0 ? (StopLossPrice - EntryPrice) / EntryPrice * 100 : 0;

    public string UpSideDisplay   => $"+{UpSidePct:F1}%";
    public string DownSideDisplay => $"{DownSidePct:F1}%";
}
