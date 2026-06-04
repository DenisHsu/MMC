namespace FibonacciMVC.Models;

public class FibonacciLevel
{
    public string Label { get; set; } = string.Empty;
    public double Ratio { get; set; }
    public double Price { get; set; }
    public double DiffFromCurrent { get; set; }
    public double DiffPercent { get; set; }

    /// <summary>現價落在此水平線附近（上下各一條）時標記</summary>
    public bool IsInZone { get; set; }

    // ── 顯示用屬性 ────────────────────────────────────────────

    public string DiffCssClass =>
        Math.Abs(DiffFromCurrent) < 0.005
            ? "diff-zero"
            : DiffFromCurrent > 0 ? "diff-up" : "diff-down";

    public string DiffDisplayText
    {
        get
        {
            if (Math.Abs(DiffFromCurrent) < 0.005) return "← 現價";
            return DiffFromCurrent > 0
                ? $"▲ +{DiffFromCurrent:F2}（+{DiffPercent:F2}%）"
                : $"▼ {DiffFromCurrent:F2}（{DiffPercent:F2}%）";
        }
    }

    public string BarWidthStyle =>
        $"width:{Ratio * 100:F0}%";
}
