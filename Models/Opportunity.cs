
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AcxiomCRM.Models;

public class Opportunity
{
    public int OpportunityId { get; set; }

    [Required]
    [StringLength(150)]
    public string OpportunityName { get; set; } = string.Empty;

    public int? CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public int? LeadId { get; set; }

    public Lead? Lead { get; set; }

    [Range(typeof(decimal), "0.01", "999999999999",
        ErrorMessage = "Opportunity amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required]
    [StringLength(30)]
    public string Stage { get; set; } = "Qualification";

    [Range(0, 100)]
    public int Probability { get; set; }

    public DateTime ExpectedCloseDate { get; set; }

    [Required]
    [StringLength(30)]
    public string Status { get; set; } = "Open";

    [Required]
    public string AssignedTo { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    [StringLength(1000)]
    public string? Notes { get; set; }

    [NotMapped]
    public decimal WeightedPipeline =>
        Amount * Probability / 100m;
}
