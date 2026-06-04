using FibonacciMVC.Models;

namespace FibonacciMVC.Services;

public interface IStockAnalysisService
{
    /// <summary>完整個股分析：費波那契 + 三大法人 + 融資券 + 評分</summary>
    Task<StockViewModel> GetFullAnalysisAsync(string stockCode);

    /// <summary>篩選建議買入的前N檔個股（首頁用）</summary>
    Task<HomeViewModel> GetTopRecommendationsAsync(int count = 5);
}
