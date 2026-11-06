
using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.Models.Api;

public class OpportunityCreateRequest
{
    [Required]
    [StringLength(150)]
    public string OpportunityName { get; set; } = string.Empty;

    public int? CustomerId { get; set; }

    public int? LeadId { get; set; }

    [Range(typeof(decimal), "0.01", "999999999999",
        ErrorMessage = "Opportunity amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required]
    [RegularExpression(
        "^(Qualification|Proposal|Negotiation|Won|Lost)$",
        ErrorMessage = "Invalid opportunity stage.")]
    public string Stage { get; set; } = "Qualification";

    [Range(0, 100,
        ErrorMessage = "Probability must be between 0 and 100.")]
    public int Probability { get; set; }

    [Required]
    public DateTime ExpectedCloseDate { get; set; }

    [Required]
    [RegularExpression(
        "^(Open|Won|Lost)$",
        ErrorMessage = "Status must be Open, Won or Lost.")]
    public string Status { get; set; } = "Open";

    [StringLength(1000)]
    public string? Notes { get; set; }
}

public class OpportunityApiResponse
{
    public int OpportunityId { get; set; }
    public string OpportunityName { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public int? LeadId { get; set; }
    public string? LeadName { get; set; }
    public decimal Amount { get; set; }
    public string Stage { get; set; } = string.Empty;
    public int Probability { get; set; }
    public decimal WeightedPipeline { get; set; }
    public DateTime ExpectedCloseDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string AssignedTo { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public string? Notes { get; set; }
}
