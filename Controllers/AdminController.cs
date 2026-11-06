
using AcxiomCRM.Models;
using AcxiomCRM.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AcxiomCRM.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    private static readonly string[] AllowedRoles =
    {
        "Admin",
        "Manager",
        "SalesExecutive"
    };

    public AdminController(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    // GET: /Admin
    public async Task<IActionResult> Index(string? search)
    {
        var users = _userManager.Users.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();

            users = users.Where(user =>
                (user.FullName ?? "").Contains(
                    search, StringComparison.OrdinalIgnoreCase) ||
                (user.Email ?? "").Contains(
                    search, StringComparison.OrdinalIgnoreCase));
        }

        var result = new List<AdminUserViewModel>();

        foreach (var user in users.OrderBy(user => user.FullName))
        {
            var roles = await _userManager.GetRolesAsync(user);

            result.Add(new AdminUserViewModel
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email ?? string.Empty,
                CurrentRole = roles.FirstOrDefault() ?? "No role",
                SelectedRole = roles.FirstOrDefault() ?? "SalesExecutive",
                IsActive = user.IsActive,
                CreatedDate = user.CreatedDate,
                ManagerId = user.ManagerId
            });
        }

        ViewBag.Search = search;

        return View(result);
    }

    // GET: /Admin/Details/{id}
    public async Task<IActionResult> Details(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        var user = await _userManager.FindByIdAsync(id);

        if (user == null)
            return NotFound();

        var roles = await _userManager.GetRolesAsync(user);

        var manager = string.IsNullOrWhiteSpace(user.ManagerId)
            ? null
            : await _userManager.FindByIdAsync(user.ManagerId);

        var model = new AdminUserViewModel
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            CurrentRole = roles.FirstOrDefault() ?? "No role",
            SelectedRole = roles.FirstOrDefault() ?? "SalesExecutive",
            IsActive = user.IsActive,
            CreatedDate = user.CreatedDate,
            ManagerId = user.ManagerId
        };

        ViewBag.ManagerName = manager?.FullName ?? "Not assigned";

        return View(model);
    }

    // GET: /Admin/Edit/{id}
    public async Task<IActionResult> Edit(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        var user = await _userManager.FindByIdAsync(id);

        if (user == null)
            return NotFound();

        var roles = await _userManager.GetRolesAsync(user);

        var model = new AdminUserViewModel
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            CurrentRole = roles.FirstOrDefault() ?? "No role",
            SelectedRole = roles.FirstOrDefault() ?? "SalesExecutive",
            IsActive = user.IsActive,
            CreatedDate = user.CreatedDate,
            ManagerId = user.ManagerId
        };

        await LoadEditOptionsAsync();

        return View(model);
    }

    // POST: /Admin/Edit
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(AdminUserViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Id))
            return NotFound();

        var user = await _userManager.FindByIdAsync(model.Id);

        if (user == null)
            return NotFound();

        // Validate Full Name.
        if (string.IsNullOrWhiteSpace(model.FullName))
        {
            ModelState.AddModelError(
                nameof(model.FullName),
                "Full Name is required.");
        }
        else
        {
            model.FullName = model.FullName.Trim();
        }

        // Validate selected role.
        if (string.IsNullOrWhiteSpace(model.SelectedRole) ||
            !AllowedRoles.Contains(model.SelectedRole))
        {
            ModelState.AddModelError(
                nameof(model.SelectedRole),
                "Select a valid role.");
        }
        else if (!await _roleManager.RoleExistsAsync(model.SelectedRole))
        {
            ModelState.AddModelError(
                nameof(model.SelectedRole),
                "The selected role does not exist.");
        }

        // Prevent an Admin from removing their own Admin access.
        var currentUserId = _userManager.GetUserId(User);
        var isEditingSelf = user.Id == currentUserId;

        if (isEditingSelf &&
            (model.SelectedRole != "Admin" || !model.IsActive))
        {
            ModelState.AddModelError(
                string.Empty,
                "You cannot remove your own Admin role or deactivate your own account.");
        }

        // Validate Manager assignment.
        if (model.SelectedRole == "SalesExecutive" &&
            !string.IsNullOrWhiteSpace(model.ManagerId))
        {
            if (model.ManagerId == user.Id)
            {
                ModelState.AddModelError(
                    nameof(model.ManagerId),
                    "A user cannot be their own manager.");
            }
            else
            {
                var manager = await _userManager.FindByIdAsync(
                    model.ManagerId);

                if (manager == null || !manager.IsActive)
                {
                    ModelState.AddModelError(
                        nameof(model.ManagerId),
                        "Select an active Manager.");
                }
                else
                {
                    var managerRoles =
                        await _userManager.GetRolesAsync(manager);

                    if (!managerRoles.Contains("Manager"))
                    {
                        ModelState.AddModelError(
                            nameof(model.ManagerId),
                            "The selected user does not have the Manager role.");
                    }
                }
            }
        }

        if (!ModelState.IsValid)
        {
            await LoadEditOptionsAsync();
            return View(model);
        }

        // Read existing roles.
        var existingRoles = await _userManager.GetRolesAsync(user);

        // Update user information.
        user.FullName = model.FullName;
        user.IsActive = model.IsActive;

        // Only SalesExecutives can have an assigned Manager.
        user.ManagerId = model.SelectedRole == "SalesExecutive"
            ? model.ManagerId
            : null;

        var updateResult = await _userManager.UpdateAsync(user);

        if (!updateResult.Succeeded)
        {
            AddIdentityErrors(updateResult);
            await LoadEditOptionsAsync();
            return View(model);
        }

        // Add the selected role first.
        bool newRoleAdded = false;

        if (!existingRoles.Contains(model.SelectedRole))
        {
            var addResult = await _userManager.AddToRoleAsync(
                user, model.SelectedRole);

            if (!addResult.Succeeded)
            {
                AddIdentityErrors(addResult);
                await LoadEditOptionsAsync();
                return View(model);
            }

            newRoleAdded = true;
        }

        // Remove all roles other than the selected role.
        var rolesToRemove = existingRoles
            .Where(role => role != model.SelectedRole)
            .ToList();

        if (rolesToRemove.Count > 0)
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(
                user, rolesToRemove);

            if (!removeResult.Succeeded)
            {
                AddIdentityErrors(removeResult);

                // Attempt to undo the newly added role if cleanup failed.
                if (newRoleAdded)
                {
                    await _userManager.RemoveFromRoleAsync(
                        user, model.SelectedRole);
                }

                await LoadEditOptionsAsync();
                return View(model);
            }
        }

        TempData["Success"] =
            "User details, role and manager assignment updated successfully.";

        return RedirectToAction(nameof(Index));
    }

    // Populate role and manager dropdowns.
    private async Task LoadEditOptionsAsync()
    {
        ViewBag.Roles = AllowedRoles;

        var managers = await _userManager.GetUsersInRoleAsync("Manager");

        ViewBag.Managers = managers
            .Where(manager => manager.IsActive)
            .OrderBy(manager => manager.FullName)
            .Select(manager => new
            {
                Id = manager.Id,
                FullName = $"{manager.FullName} ({manager.Email})"
            })
            .ToList();
    }

    // Display Identity errors in the form.
    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(
                string.Empty,
                error.Description);
        }
    }
}
