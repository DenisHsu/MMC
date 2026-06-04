namespace FibonacciMVC.Models;

/// <summary>單日融資融券資料</summary>
public class MarginRecord
{
    public string Date { get; set; } = string.Empty;

    /// <summary>融資餘額（股）</summary>
    public long MarginBalance { get; set; }
    /// <summary>融資前日餘額（股）</summary>
    public long MarginPrevBalance { get; set; }
    /// <summary>融券餘額（股）</summary>
    public long ShortBalance { get; set; }
    /// <summary>融券前日餘額（股）</summary>
    public long ShortPrevBalance { get; set; }

    // ── 計算欄位 ───────────────────────────────────────────────────

    public long MarginChange => MarginBalance - MarginPrevBalance;
    public long ShortChange  => ShortBalance  - ShortPrevBalance;

    /// <summary>融券比 = 融券餘額 / 融資餘額（%），高代表放空力道強</summary>
    public double ShortRatio =>
        MarginBalance > 0 ? (double)ShortBalance / MarginBalance * 100 : 0;

    // ── 顯示用 ─────────────────────────────────────────────────────

    public string MarginBalanceDisplay  => $"{MarginBalance / 1000:N0} 張";
    public string ShortBalanceDisplay   => $"{ShortBalance / 1000:N0} 張";
    public string MarginChangeDisplay   => FormatChange(MarginChange / 1000);
    public string ShortChangeDisplay    => FormatChange(ShortChange / 1000);

    public string MarginChangeCssClass  => ChangeClass(MarginChange);
    public string ShortChangeCssClass   => ChangeClass(ShortChange);

    // 融資餘額下降 → 對多方有利（去槓桿）→ 用綠色標示
    public string MarginTrendCssClass   => MarginChange < 0 ? "net-buy" : "net-sell";
    public string ShortTrendCssClass    => ShortChange  > 0 ? "net-buy" : "net-sell";

    private static string FormatChange(long k) =>
        k == 0 ? "—" : k > 0 ? $"+{k:N0} 張" : $"{k:N0} 張";

    private static string ChangeClass(long v) =>
        v > 0 ? "net-buy" : v < 0 ? "net-sell" : "net-zero";
}
