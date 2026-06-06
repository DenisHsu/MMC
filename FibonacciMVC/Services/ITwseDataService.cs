using FibonacciMVC.Models;

namespace FibonacciMVC.Services;

public interface ITwseDataService
{
    /// <summary>取得單一股票三大法人近N日資料（TWSE TWT38U）</summary>
    Task<List<InstitutionalRecord>> GetInstitutionalHistoryAsync(string stockCode, int days = 5);

    /// <summary>取得單一股票最新融資融券資料（TWSE MI_MARGN）</summary>
    Task<MarginRecord?> GetMarginDataAsync(string stockCode);

    /// <summary>取得全市場三大法人買賣超，回傳 Dict[code → (外資,投信,合計)]（TWSE T86）</summary>
    Task<Dictionary<string, InstitutionalSummary>> GetAllInstitutionalAsync();

    /// <summary>從快取取得股票中文名稱（由 T86 / TWT38U API 解析後寫入）</summary>
    string? GetChineseName(string stockCode);

    /// <summary>
    /// 將使用者輸入解析為正確的股票代號。
    /// 純數字 → 直接回傳；中文名稱 → 查詢名稱字典後回傳代號；找不到回傳 null。
    /// </summary>
    Task<string?> ResolveStockCodeAsync(string query);
}

public record InstitutionalSummary(long ForeignNet, long InvestTrustNet, long TotalNet);
