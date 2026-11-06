
using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.Models.ViewModels;

public class AdminUserViewModel
{
    public string Id { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string CurrentRole { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please select a role.")]
    public string SelectedRole { get; set; } = "SalesExecutive";

    public bool IsActive { get; set; }

    public DateTime CreatedDate { get; set; }

    // Manager assigned to a SalesExecutive.
    public string? ManagerId { get; set; }
}
