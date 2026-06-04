namespace FibonacciMVC.Models;

public enum Recommendation { Buy, Watch, Avoid, Insufficient }

/// <summary>個股綜合評分與買賣建議</summary>
public class AnalysisScore
{
    // ── 各項評分 ─────────────────────────────────────────────────

    /// <summary>外資動向 0-30</summary>
    public int ForeignScore { get; set; }
    /// <summary>投信動向 0-20</summary>
    public int InvestmentTrustScore { get; set; }
    /// <summary>融資券動向 0-20</summary>
    public int MarginScore { get; set; }
    /// <summary>費波那契位置 0-30</summary>
    public int FibonacciScore { get; set; }

    public int Total => ForeignScore + InvestmentTrustScore + MarginScore + FibonacciScore;

    // ── 建議 ──────────────────────────────────────────────────────

    public Recommendation Recommendation =>
        Total >= 70 ? Recommendation.Buy :
        Total >= 50 ? Recommendation.Watch :
                      Recommendation.Avoid;

    public string RecommendationText => Recommendation switch
    {
        Recommendation.Buy   => "建議買入",
        Recommendation.Watch => "觀望",
        Recommendation.Avoid => "暫不建議",
        _                    => "資料不足",
    };

    public string RecommendationCssClass => Recommendation switch
    {
        Recommendation.Buy   => "rec-buy",
        Recommendation.Watch => "rec-watch",
        Recommendation.Avoid => "rec-avoid",
        _                    => "rec-insufficient",
    };

    // ── 各項說明 ──────────────────────────────────────────────────

    public string ForeignReason         { get; set; } = string.Empty;
    public string InvestmentTrustReason { get; set; } = string.Empty;
    public string MarginReason          { get; set; } = string.Empty;
    public string FibonacciReason       { get; set; } = string.Empty;

    // ── 買賣點 ────────────────────────────────────────────────────

    /// <summary>建議買點（費波那契支撐）</summary>
    public double EntryPrice    { get; set; }
    /// <summary>目標價（費波那契壓力）</summary>
    public double TargetPrice   { get; set; }
    /// <summary>停損價（支撐下一層）</summary>
    public double StopLossPrice { get; set; }

    public double UpSidePct   => EntryPrice > 0 ? (TargetPrice   - EntryPrice) / EntryPrice * 100 : 0;
    public double DownSidePct => EntryPrice > 0 ? (StopLossPrice - EntryPrice) / EntryPrice * 100 : 0;
}
