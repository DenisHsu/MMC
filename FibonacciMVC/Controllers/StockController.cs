using FibonacciMVC.Models;
using FibonacciMVC.Services;
using Microsoft.AspNetCore.Mvc;

namespace FibonacciMVC.Controllers;

public class StockController : Controller
{
    private readonly IStockAnalysisService _analysisService;

    public StockController(IStockAnalysisService analysisService)
    {
        _analysisService = analysisService;
    }

    // GET /Stock  或  GET /Stock?stockCode=2330（從首頁連結點入時直接顯示分析結果）
    [HttpGet]
    public async Task<IActionResult> Index(string? stockCode)
    {
        if (string.IsNullOrWhiteSpace(stockCode))
            return View(new StockViewModel());

        var vm = await _analysisService.GetFullAnalysisAsync(stockCode.Trim().ToUpper());
        return View(vm);
    }

    // POST /Stock/Analyze
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Analyze(string? stockCode)
    {
        if (string.IsNullOrWhiteSpace(stockCode))
        {
            return View("Index", new StockViewModel { ErrorMessage = "請輸入股票代號" });
        }

        var vm = await _analysisService.GetFullAnalysisAsync(stockCode.Trim().ToUpper());
        return View("Index", vm);
    }
}
