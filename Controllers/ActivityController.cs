using AcxiomCRM.Data;
using AcxiomCRM.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AcxiomCRM.Controllers;

[Authorize(Roles = "Admin,Manager,SalesExecutive")]
public class ActivityController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    private static readonly string[] ValidTypes =
        { "Call", "Meeting", "Email", "Task" };

    private static readonly string[] ValidStatuses =
        { "Planned", "Completed", "Missed", "Cancelled" };

    public ActivityController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    private string CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private bool IsAdmin => User.IsInRole("Admin");

    // GET: /Activity
    public async Task<IActionResult> Index(string? search, string? type, string? status)
    {
        var query = GetAccessibleActivities()
            .Include(a => a.Customer)
            .Include(a => a.Lead)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();

            query = query.Where(a =>
                a.Subject.Contains(search) ||
                (a.Description != null && a.Description.Contains(search)) ||
                (a.Customer != null &&
                    a.Customer.CustomerName.Contains(search)) ||
                (a.Lead != null &&
                    a.Lead.LeadName.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(a => a.ActivityType == type);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(a => a.Status == status);

        ViewBag.Search = search;
        ViewBag.Type = type;
        ViewBag.Status = status;
        ViewBag.Types = new SelectList(ValidTypes, type);
        ViewBag.Statuses = new SelectList(ValidStatuses, status);

        var activities = await query
            .OrderByDescending(a => a.ActivityDate)
            .ToListAsync();

        return View(activities);
    }

    // GET: /Activity/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
            return NotFound();

        var activity = await GetAccessibleActivities()
            .Include(a => a.Customer)
            .Include(a => a.Lead)
            .FirstOrDefaultAsync(a => a.ActivityId == id);

        if (activity == null)
            return NotFound();

        return View(activity);
    }

    // GET: /Activity/Create
    public async Task<IActionResult> Create()
    {
        await LoadFormDataAsync();

        return View(new Activity
        {
            ActivityDate = DateTime.Now,
            ActivityType = "Call",
            Status = "Completed"
        });
    }

    // POST: /Activity/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Activity input)
    {
        ValidateActivity(input);
        await ValidateLinksAsync(input);

        if (IsAdmin)
        {
            if (string.IsNullOrWhiteSpace(input.AssignedTo))
            {
                ModelState.AddModelError(
                    nameof(input.AssignedTo),
                    "Select a user.");
            }
            else
            {
                var assignedUser =
                    await _userManager.FindByIdAsync(input.AssignedTo);

                if (assignedUser == null || !assignedUser.IsActive)
                {
                    ModelState.AddModelError(
                        nameof(input.AssignedTo),
                        "Select an active user.");
                }
            }
        }
        else
        {
            input.AssignedTo = CurrentUserId;
            ModelState.Remove(nameof(input.AssignedTo));
        }

        if (!ModelState.IsValid)
        {
            await LoadFormDataAsync(
                input.CustomerId, input.LeadId, input.AssignedTo);

            return View(input);
        }

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            _context.Activities.Add(input);
            await _context.SaveChangesAsync();

            AddAudit(
                "Create",
                input.ActivityId,
                null,
                $"Created activity: {input.Subject}");

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = "Activity created successfully.";

            return RedirectToAction(nameof(Index));
        }
        catch
        {
            await transaction.RollbackAsync();

            ModelState.AddModelError(
                string.Empty,
                "The activity could not be saved. Please try again.");

            await LoadFormDataAsync(
                input.CustomerId, input.LeadId, input.AssignedTo);

            return View(input);
        }
    }

    // GET: /Activity/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
            return NotFound();

        var activity = await GetAccessibleActivities()
            .FirstOrDefaultAsync(a => a.ActivityId == id);

        if (activity == null)
            return NotFound();

        await LoadFormDataAsync(
            activity.CustomerId,
            activity.LeadId,
            activity.AssignedTo);

        return View(activity);
    }

    // POST: /Activity/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Activity input)
    {
        if (id != input.ActivityId)
            return NotFound();

        var existing = await GetAccessibleActivities()
            .FirstOrDefaultAsync(a => a.ActivityId == id);

        if (existing == null)
            return NotFound();

        ValidateActivity(input);
        await ValidateLinksAsync(input);

        if (IsAdmin)
        {
            if (string.IsNullOrWhiteSpace(input.AssignedTo))
            {
                ModelState.AddModelError(
                    nameof(input.AssignedTo),
                    "Select a user.");
            }
            else
            {
                var assignedUser =
                    await _userManager.FindByIdAsync(input.AssignedTo);

                if (assignedUser == null || !assignedUser.IsActive)
                {
                    ModelState.AddModelError(
                        nameof(input.AssignedTo),
                        "Select an active user.");
                }
            }
        }
        else
        {
            input.AssignedTo = existing.AssignedTo;
            ModelState.Remove(nameof(input.AssignedTo));
        }

        if (!ModelState.IsValid)
        {
            await LoadFormDataAsync(
                input.CustomerId, input.LeadId, input.AssignedTo);

            return View(input);
        }

        var oldValue =
            $"Subject: {existing.Subject}; Type: {existing.ActivityType}; " +
            $"Status: {existing.Status}";

        existing.ActivityType = input.ActivityType;
        existing.Subject = input.Subject;
        existing.Description = input.Description;
        existing.ActivityDate = input.ActivityDate;
        existing.CustomerId = input.CustomerId;
        existing.LeadId = input.LeadId;
        existing.Status = input.Status;

        if (IsAdmin)
            existing.AssignedTo = input.AssignedTo;

        var newValue =
            $"Subject: {existing.Subject}; Type: {existing.ActivityType}; " +
            $"Status: {existing.Status}";

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            AddAudit(
                "Update",
                existing.ActivityId,
                oldValue,
                newValue);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = "Activity updated successfully.";

            return RedirectToAction(nameof(Index));
        }
        catch
        {
            await transaction.RollbackAsync();

            ModelState.AddModelError(
                string.Empty,
                "The activity could not be updated. Please try again.");

            await LoadFormDataAsync(
                input.CustomerId, input.LeadId, input.AssignedTo);

            return View(input);
        }
    }

    // GET: /Activity/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
            return NotFound();

        var activity = await GetAccessibleActivities()
            .Include(a => a.Customer)
            .Include(a => a.Lead)
            .FirstOrDefaultAsync(a => a.ActivityId == id);

        if (activity == null)
            return NotFound();

        return View(activity);
    }

    // POST: /Activity/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var activity = await GetAccessibleActivities()
            .FirstOrDefaultAsync(a => a.ActivityId == id);

        if (activity == null)
            return NotFound();

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            var oldValue =
                $"Subject: {activity.Subject}; Type: {activity.ActivityType}; " +
                $"Status: {activity.Status}";

            _context.Activities.Remove(activity);

            AddAudit("Delete", id, oldValue, null);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = "Activity deleted successfully.";

            return RedirectToAction(nameof(Index));
        }
        catch
        {
            await transaction.RollbackAsync();

            TempData["Error"] =
                "The activity could not be deleted. Please try again.";

            return RedirectToAction(nameof(Index));
        }
    }

    // Restrict records to the current user unless Admin.
    private IQueryable<Activity> GetAccessibleActivities()
    {
        var query = _context.Activities.AsQueryable();

        if (!IsAdmin)
            query = query.Where(a => a.AssignedTo == CurrentUserId);

        return query;
    }

    private void ValidateActivity(Activity activity)
    {
        if (!ValidTypes.Contains(activity.ActivityType))
        {
            ModelState.AddModelError(
                nameof(activity.ActivityType),
                "Select a valid activity type.");
        }

        if (!ValidStatuses.Contains(activity.Status))
        {
            ModelState.AddModelError(
                nameof(activity.Status),
                "Select a valid activity status.");
        }
    }

    // Exactly one customer or lead must be selected.
    private async Task<bool> ValidateLinksAsync(Activity activity)
    {
        if (activity.CustomerId.HasValue == activity.LeadId.HasValue)
        {
            ModelState.AddModelError(
                string.Empty,
                "Select exactly one customer or one lead.");

            return false;
        }

        if (activity.CustomerId.HasValue)
        {
            var customerQuery = _context.Customers
                .Where(c => c.CustomerId == activity.CustomerId.Value);

            if (!IsAdmin)
            {
                customerQuery = customerQuery
                    .Where(c => c.CreatedBy == CurrentUserId);
            }

            if (!await customerQuery.AnyAsync())
            {
                ModelState.AddModelError(
                    nameof(activity.CustomerId),
                    "The selected customer does not exist or is not accessible.");

                return false;
            }
        }

        if (activity.LeadId.HasValue)
        {
            var leadQuery = _context.Leads
                .Where(l => l.LeadId == activity.LeadId.Value);

            if (!IsAdmin)
            {
                leadQuery = leadQuery
                    .Where(l => l.AssignedTo == CurrentUserId);
            }

            if (!await leadQuery.AnyAsync())
            {
                ModelState.AddModelError(
                    nameof(activity.LeadId),
                    "The selected lead does not exist or is not accessible.");

                return false;
            }
        }

        return true;
    }

    private async Task LoadFormDataAsync(
        int? selectedCustomerId = null,
        int? selectedLeadId = null,
        string? selectedUserId = null)
    {
        var customers = _context.Customers.AsQueryable();
        var leads = _context.Leads.AsQueryable();

        if (!IsAdmin)
        {
            customers = customers
                .Where(c => c.CreatedBy == CurrentUserId);

            leads = leads
                .Where(l => l.AssignedTo == CurrentUserId);
        }

        ViewBag.Customers = new SelectList(
            await customers.OrderBy(c => c.CustomerName).ToListAsync(),
            "CustomerId",
            "CustomerName",
            selectedCustomerId);

        ViewBag.Leads = new SelectList(
            await leads.OrderBy(l => l.LeadName).ToListAsync(),
            "LeadId",
            "LeadName",
            selectedLeadId);

        ViewBag.Types = new SelectList(ValidTypes);
        ViewBag.Statuses = new SelectList(ValidStatuses);

        if (IsAdmin)
        {
            var users = await _userManager.Users
                .Where(u => u.IsActive)
                .OrderBy(u => u.FullName)
                .ToListAsync();

            ViewBag.Users = new SelectList(
                users,
                "Id",
                "FullName",
                selectedUserId);
        }
    }

    private void AddAudit(
        string action,
        int recordId,
        string? oldValue,
        string? newValue)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = CurrentUserId,
            Action = action,
            EntityName = "Activity",
            RecordId = recordId,
            OldValue = oldValue,
            NewValue = newValue,
            CreatedDate = DateTime.UtcNow,
            Result = "Success",
            Details = $"{action} activity record {recordId}"
        });
    }
}