
using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.Models.Api;

public class LeadCreateRequest
{
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
    [RegularExpression(
        "^(New|Contacted|Qualified|Unqualified|Lost)$",
        ErrorMessage = "Invalid lead status.")]
    public string Status { get; set; } = "New";

    [Required]
    [RegularExpression(
        "^(Low|Medium|High)$",
        ErrorMessage = "Priority must be Low, Medium or High.")]
    public string Priority { get; set; } = "Medium";

    [Range(typeof(decimal), "0", "999999999999",
        ErrorMessage = "Expected value cannot be negative.")]
    public decimal ExpectedValue { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }
}

public class LeadApiResponse
{
    public int LeadId { get; set; }
    public string LeadCode { get; set; } = string.Empty;
    public string LeadName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? CompanyName { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public decimal ExpectedValue { get; set; }
    public string AssignedTo { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public string? Notes { get; set; }
}
