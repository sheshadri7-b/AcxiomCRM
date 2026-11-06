using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.Models.ViewModels;

public class LeadConversionViewModel
{
    public int LeadId { get; set; }

    [Required]
    [StringLength(150)]
    public string OpportunityName { get; set; } = string.Empty;

    [Required]
    [Range(typeof(decimal), "0.01", "999999999999",
        ErrorMessage = "Opportunity amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Range(0, 100)]
    public int Probability { get; set; } = 25;

    [Required]
    [DataType(DataType.Date)]
    public DateTime ExpectedCloseDate { get; set; } =
        DateTime.Today.AddDays(30);

    [StringLength(1000)]
    public string? Notes { get; set; }
}
