
using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.Models;

public class FollowUp
{
    public int FollowUpId { get; set; }

    public int? CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public int? LeadId { get; set; }

    public Lead? Lead { get; set; }

    [Required]
    public DateTime FollowUpDate { get; set; }

    [Required]
    [StringLength(100)]
    public string FollowUpType { get; set; } = "Call";

    [Required]
    [StringLength(150)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    [StringLength(30)]
    public string Status { get; set; } = "Planned";

    [Required]
    public string AssignedTo { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Remarks { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
