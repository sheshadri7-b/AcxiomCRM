using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.Models.Api;

public class FollowUpCreateRequest
{
    public int? CustomerId { get; set; }

    public int? LeadId { get; set; }

    [Required]
    public DateTime FollowUpDate { get; set; }

    [Required]
    [RegularExpression(
        "^(Call|Meeting|Email|Task)$",
        ErrorMessage = "Follow-up type must be Call, Meeting, Email, or Task."
    )]
    public string FollowUpType { get; set; } = "Call";

    [Required]
    [StringLength(150)]
    public string Subject { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Remarks { get; set; }
}

public class FollowUpApiResponse
{
    public int FollowUpId { get; set; }

    public int? CustomerId { get; set; }

    public string? CustomerName { get; set; }

    public int? LeadId { get; set; }

    public string? LeadName { get; set; }

    public DateTime FollowUpDate { get; set; }

    public string FollowUpType { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string AssignedTo { get; set; } = string.Empty;

    public string? Remarks { get; set; }

    public DateTime CreatedDate { get; set; }
}