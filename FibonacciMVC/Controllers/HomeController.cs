using System.Diagnostics;
using FibonacciMVC.Models;
using FibonacciMVC.Services;
using Microsoft.AspNetCore.Mvc;

namespace FibonacciMVC.Controllers;

public class HomeController : Controller
{
    private readonly IStockAnalysisService _analysisService;

    public HomeController(IStockAnalysisService analysisService)
    {
        _analysisService = analysisService;
    }

    // GET /
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var vm = await _analysisService.GetTopRecommendationsAsync(5);
        return View(vm);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }
}
