
using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.Models;

public class Lead
{
    public int LeadId { get; set; }

    [Required]
    [StringLength(30)]
    public string LeadCode { get; set; } = string.Empty;

    [Required]
    [StringLength(150)]
    public string LeadName { get; set; } = string.Empty;

    [EmailAddress]
    [StringLength(254)]
    public string? Email { get; set; }

    [Phone]
    [StringLength(20)]
    public string? Phone { get; set; }

    [StringLength(150)]
    public string? CompanyName { get; set; }

    [Required]
    [StringLength(100)]
    public string Source { get; set; } = string.Empty;

    [Required]
    [StringLength(30)]
    public string Status { get; set; } = "New";

    [StringLength(30)]
    public string Priority { get; set; } = "Medium";
[Range(typeof(decimal), "0", "999999999999",
    ErrorMessage = "Expected value cannot be negative.")]
public decimal ExpectedValue { get; set; }

    [Required]
    public string AssignedTo { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    [StringLength(1000)]
    public string? Notes { get; set; }

    public int? ConvertedCustomerId { get; set; }

    public Customer? ConvertedCustomer { get; set; }

    public ICollection<FollowUp> FollowUps { get; set; }
        = new List<FollowUp>();

    public ICollection<Activity> Activities { get; set; }
        = new List<Activity>();
}
