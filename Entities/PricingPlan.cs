namespace PKeetDashboard.API.Entities;

public class PricingPlan
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal MonthlyPrice { get; set; }
    public decimal? YearlyPrice { get; set; }
    public int? YearlyDiscountPercent { get; set; }
    public bool IsHighlighted { get; set; }
    public string? ButtonText { get; set; }
    public string? ButtonUrl { get; set; }
    public int SortOrder { get; set; }

    public ICollection<PlanFeature> Features { get; set; } = new List<PlanFeature>();
}
