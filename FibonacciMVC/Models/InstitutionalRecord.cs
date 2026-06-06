namespace FibonacciMVC.Models;

/// <summary>單日三大法人買賣超紀錄（原始單位：股）</summary>
public class InstitutionalRecord
{
    public string Date { get; set; } = string.Empty;

    /// <summary>外資買賣超（股）</summary>
    public long ForeignNet { get; set; }
    /// <summary>投信買賣超（股）</summary>
    public long InvestmentTrustNet { get; set; }
    /// <summary>自營商買賣超（股）</summary>
    public long DealerNet { get; set; }
    /// <summary>三大法人合計買賣超（股）</summary>
    public long TotalNet { get; set; }

    // ── 顯示用（換算成張，1張=1000股）───────────────────────────

    public long ForeignNetK     => ForeignNet / 1000;
    public long InvestTrustNetK => InvestmentTrustNet / 1000;
    public long TotalNetK       => TotalNet / 1000;

    public string ForeignDisplay     => FormatK(ForeignNetK);
    public string InvestTrustDisplay => FormatK(InvestTrustNetK);
    public string TotalDisplay       => FormatK(TotalNetK);

    public string ForeignCssClass     => NetClass(ForeignNet);
    public string InvestTrustCssClass => NetClass(InvestmentTrustNet);
    public string TotalCssClass       => NetClass(TotalNet);

    private static string FormatK(long k) =>
        k == 0 ? "—" : k > 0 ? $"+{k:N0}" : $"{k:N0}";

    private static string NetClass(long v) =>
        v > 0 ? "net-buy" : v < 0 ? "net-sell" : "net-zero";
}
