
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.Models;

public class ApplicationUser : IdentityUser
{
    [Required]
    [StringLength(100)]
    public string FullName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    // Manager assigned to this user.
    // Null means the user has no assigned manager.
    public string? ManagerId { get; set; }

    public ApplicationUser? Manager { get; set; }

    // Employees reporting to this manager.
    public ICollection<ApplicationUser> TeamMembers { get; set; }
        = new List<ApplicationUser>();
}
