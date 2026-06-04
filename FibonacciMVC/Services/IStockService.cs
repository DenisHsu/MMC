using FibonacciMVC.Models;

namespace FibonacciMVC.Services;

public interface IStockService
{
    /// <summary>
    /// 依股票代號取得當日高低點，並計算費波那契黃金分割位。
    /// </summary>
    Task<StockViewModel> GetStockDataAsync(string stockCode);
}
