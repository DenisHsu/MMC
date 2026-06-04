namespace FibonacciMVC.Models;

public class StockViewModel
{
    // ── 查詢輸入 ─────────────────────────────────────────────
    public string? Code { get; set; }

    // ── 股票資訊（Yahoo Finance）──────────────────────────────
    public string? Name   { get; set; }
    public string? Symbol { get; set; }
    public double  CurrentPrice { get; set; }
    public double  DayHigh      { get; set; }
    public double  DayLow       { get; set; }
    public DateTime? QueryTime  { get; set; }

    // ── 費波那契計算欄位 ──────────────────────────────────────
    public double DayRange        => DayHigh - DayLow;
    public double DayRangePercent => DayLow > 0 ? DayRange / DayLow * 100 : 0;

    /// <summary>現價在當日區間的相對位置（0 = 低點，1 = 高點）</summary>
    public double PricePositionRatio =>
        DayRange > 0 ? Math.Clamp((CurrentPrice - DayLow) / DayRange, 0d, 1d) : 0d;

    /// <summary>現價指示器 CSS top 值（梯狀圖高點在頂）</summary>
    public string MarkerTopStyle =>
        $"top: calc({(1 - PricePositionRatio) * 100:F1}% - 3px)";

    public List<FibonacciLevel> FibonacciLevels { get; set; } = [];
    public bool HasResult => FibonacciLevels.Count > 0;

    // ── 三大法人近5日 ─────────────────────────────────────────
    public List<InstitutionalRecord> InstitutionalHistory { get; set; } = [];
    public bool HasInstitutionalData => InstitutionalHistory.Count > 0;

    // ── 融資融券 ──────────────────────────────────────────────
    public MarginRecord? Margin      { get; set; }
    public bool          HasMargin   => Margin != null;

    // ── 綜合分析 ──────────────────────────────────────────────
    public AnalysisScore? Analysis   { get; set; }
    public bool           HasAnalysis => Analysis != null;

    // ── 錯誤訊息 ─────────────────────────────────────────────
    public string? ErrorMessage { get; set; }
    public bool    HasError     => !string.IsNullOrEmpty(ErrorMessage);
}
