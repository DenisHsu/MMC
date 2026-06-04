using FibonacciMVC.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// ── 記憶體快取（三大法人/融資券資料每30分鐘更新一次）────────────
builder.Services.AddMemoryCache();

// ── Yahoo Finance HttpClient（取得價格 + 費波那契）────────────────
builder.Services.AddHttpClient<IStockService, YahooFinanceService>(client =>
{
    client.DefaultRequestHeaders.Add(
        "User-Agent",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) " +
        "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
    client.DefaultRequestHeaders.Add("Accept-Language", "zh-TW,zh;q=0.9,en-US;q=0.8");
    client.DefaultRequestHeaders.Add("Referer", "https://finance.yahoo.com/");
    client.Timeout = TimeSpan.FromSeconds(15);
});

// ── TWSE HttpClient（三大法人 + 融資融券）────────────────────────
builder.Services.AddHttpClient<ITwseDataService, TwseDataService>(client =>
{
    client.DefaultRequestHeaders.Add(
        "User-Agent",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) " +
        "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
    client.Timeout = TimeSpan.FromSeconds(20);
});

// ── 分析服務（整合 Yahoo + TWSE）────────────────────────────────
builder.Services.AddScoped<IStockAnalysisService, StockAnalysisService>();

// ── Pipeline ─────────────────────────────────────────────────────
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// 預設路由：首頁 → HomeController.Index（推薦個股）
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
