namespace PKeetDashboard.API.Entities;

public class PlanFeature
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public string Text { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public PricingPlan Plan { get; set; } = null!;
}
