
namespace AcxiomCRM.Models.Api;

public class PipelineReportApiResponse
{
    public int TotalOpenOpportunities { get; set; }

    public decimal TotalPipelineValue { get; set; }

    public decimal WeightedPipelineValue { get; set; }

    public List<PipelineStageRow> StageBreakdown { get; set; } = new();

    public List<PipelineOwnerRow> OwnerBreakdown { get; set; } = new();
}

public class PipelineStageRow
{
    public string Stage { get; set; } = string.Empty;

    public int OpportunityCount { get; set; }

    public decimal PipelineValue { get; set; }

    public decimal WeightedPipelineValue { get; set; }
}

public class PipelineOwnerRow
{
    public string AssignedTo { get; set; } = string.Empty;

    public int OpportunityCount { get; set; }

    public decimal PipelineValue { get; set; }

    public decimal WeightedPipelineValue { get; set; }
}
