namespace FibonacciMVC.Models;

public class HomeViewModel
{
    public List<StockRecommendation> Recommendations { get; set; } = [];
    public DateTime? LastUpdated  { get; set; }
    public string?   ErrorMessage { get; set; }
    public bool      HasError     => !string.IsNullOrEmpty(ErrorMessage);
    public bool      HasData      => Recommendations.Count > 0;
}
