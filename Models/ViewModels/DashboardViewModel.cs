
namespace AcxiomCRM.Models.ViewModels;

public class DashboardViewModel
{
    public int TotalCustomers { get; set; }
    public int TotalLeads { get; set; }
    public int OpenLeads { get; set; }

    public int TotalOpportunities { get; set; }
    public int OpenOpportunities { get; set; }
    public int WonOpportunities { get; set; }
    public int LostOpportunities { get; set; }

    public decimal PipelineValue { get; set; }

    public Dictionary<string, int> LeadStatusCounts { get; set; } = new();

    public Dictionary<string, int> OpportunityStageCounts { get; set; } = new();

    public Dictionary<string, decimal> MonthlySales { get; set; } = new();
}
