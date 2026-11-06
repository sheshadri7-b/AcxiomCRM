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
public class FollowUpController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    private static readonly string[] ValidTypes =
        { "Call", "Meeting", "Email", "Task" };

    private static readonly string[] ValidStatuses =
        { "Planned", "Completed", "Missed", "Cancelled" };

    public FollowUpController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    private string CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private bool IsAdmin => User.IsInRole("Admin");

    // GET: /FollowUp
    public async Task<IActionResult> Index(string? search, string? status)
    {
        var query = GetAccessibleFollowUps()
            .Include(f => f.Customer)
            .Include(f => f.Lead)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();

            query = query.Where(f =>
                f.Subject.Contains(search) ||
                (f.Remarks != null && f.Remarks.Contains(search)) ||
                (f.Customer != null &&
                    f.Customer.CustomerName.Contains(search)) ||
                (f.Lead != null &&
                    f.Lead.LeadName.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(f => f.Status == status);
        }

        ViewBag.Search = search;
        ViewBag.Status = status;
        ViewBag.Statuses = new SelectList(ValidStatuses, status);

        var followUps = await query
            .OrderBy(f => f.FollowUpDate)
            .ToListAsync();

        return View(followUps);
    }

    // GET: /FollowUp/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
            return NotFound();

        var followUp = await GetAccessibleFollowUps()
            .Include(f => f.Customer)
            .Include(f => f.Lead)
            .FirstOrDefaultAsync(f => f.FollowUpId == id);

        if (followUp == null)
            return NotFound();

        return View(followUp);
    }

    // GET: /FollowUp/Create
    public async Task<IActionResult> Create()
    {
        await LoadFormDataAsync();
        return View(new FollowUp
        {
            FollowUpDate = DateTime.Today,
            Status = "Planned",
            FollowUpType = "Call"
        });
    }

    // POST: /FollowUp/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(FollowUp input)
    {
        ValidateTypeAndStatus(input);
        ValidateFollowUpDate(input);

        if (!await ValidateLinksAsync(input))
        {
            // Link errors are added to ModelState by the validation method.
        }

        if (IsAdmin)
        {
            if (string.IsNullOrWhiteSpace(input.AssignedTo) ||
                await _userManager.FindByIdAsync(input.AssignedTo) is not
                    { IsActive: true })
            {
                ModelState.AddModelError(
                    nameof(input.AssignedTo),
                    "Select an active user.");
            }
        }
        else
        {
            input.AssignedTo = CurrentUserId;
            ModelState.Remove(nameof(input.AssignedTo));
        }

        if (!ModelState.IsValid)
        {
            await LoadFormDataAsync(input.CustomerId, input.LeadId,
                input.AssignedTo);
            return View(input);
        }

        input.CreatedDate = DateTime.UtcNow;

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            _context.FollowUps.Add(input);
            await _context.SaveChangesAsync();

            AddAudit("Create", input.FollowUpId, null,
                $"Created follow-up: {input.Subject}");

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = "Follow-up created successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch
        {
            await transaction.RollbackAsync();
            ModelState.AddModelError(string.Empty,
                "The follow-up could not be saved. Please try again.");

            await LoadFormDataAsync(input.CustomerId, input.LeadId,
                input.AssignedTo);
            return View(input);
        }
    }

    // GET: /FollowUp/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
            return NotFound();

        var followUp = await GetAccessibleFollowUps()
            .FirstOrDefaultAsync(f => f.FollowUpId == id);

        if (followUp == null)
            return NotFound();

        await LoadFormDataAsync(
            followUp.CustomerId,
            followUp.LeadId,
            followUp.AssignedTo);

        return View(followUp);
    }

    // POST: /FollowUp/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, FollowUp input)
    {
        if (id != input.FollowUpId)
            return NotFound();

        var existing = await GetAccessibleFollowUps()
            .FirstOrDefaultAsync(f => f.FollowUpId == id);

        if (existing == null)
            return NotFound();

        ValidateTypeAndStatus(input);
        ValidateFollowUpDate(input);

        await ValidateLinksAsync(input);

        if (IsAdmin)
        {
            if (string.IsNullOrWhiteSpace(input.AssignedTo) ||
                await _userManager.FindByIdAsync(input.AssignedTo) is not
                    { IsActive: true })
            {
                ModelState.AddModelError(
                    nameof(input.AssignedTo),
                    "Select an active user.");
            }
        }
        else
        {
            input.AssignedTo = existing.AssignedTo;
            ModelState.Remove(nameof(input.AssignedTo));
        }

        if (!ModelState.IsValid)
        {
            await LoadFormDataAsync(input.CustomerId, input.LeadId,
                input.AssignedTo);
            return View(input);
        }

        var oldValue =
            $"Subject: {existing.Subject}; Status: {existing.Status}; " +
            $"Date: {existing.FollowUpDate:yyyy-MM-dd}";

        existing.CustomerId = input.CustomerId;
        existing.LeadId = input.LeadId;
        existing.FollowUpDate = input.FollowUpDate;
        existing.FollowUpType = input.FollowUpType;
        existing.Subject = input.Subject;
        existing.Status = input.Status;
        existing.Remarks = input.Remarks;

        if (IsAdmin)
            existing.AssignedTo = input.AssignedTo;

        var newValue =
            $"Subject: {existing.Subject}; Status: {existing.Status}; " +
            $"Date: {existing.FollowUpDate:yyyy-MM-dd}";

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            AddAudit("Update", existing.FollowUpId,
                oldValue, newValue);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = "Follow-up updated successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch
        {
            await transaction.RollbackAsync();
            ModelState.AddModelError(string.Empty,
                "The follow-up could not be updated. Please try again.");

            await LoadFormDataAsync(input.CustomerId, input.LeadId,
                input.AssignedTo);
            return View(input);
        }
    }

    // GET: /FollowUp/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
            return NotFound();

        var followUp = await GetAccessibleFollowUps()
            .Include(f => f.Customer)
            .Include(f => f.Lead)
            .FirstOrDefaultAsync(f => f.FollowUpId == id);

        if (followUp == null)
            return NotFound();

        return View(followUp);
    }

    // POST: /FollowUp/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var followUp = await GetAccessibleFollowUps()
            .FirstOrDefaultAsync(f => f.FollowUpId == id);

        if (followUp == null)
            return NotFound();

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            var oldValue =
                $"Subject: {followUp.Subject}; Status: {followUp.Status}";

            _context.FollowUps.Remove(followUp);

            AddAudit("Delete", id, oldValue, null);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = "Follow-up deleted successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch
        {
            await transaction.RollbackAsync();

            TempData["Error"] =
                "The follow-up could not be deleted. Please try again.";

            return RedirectToAction(nameof(Index));
        }
    }

    // Returns only records the current user is allowed to access.
    private IQueryable<FollowUp> GetAccessibleFollowUps()
    {
        var query = _context.FollowUps.AsQueryable();

        if (!IsAdmin)
            query = query.Where(f => f.AssignedTo == CurrentUserId);

        return query;
    }

    private void ValidateTypeAndStatus(FollowUp followUp)
    {
        if (!ValidTypes.Contains(followUp.FollowUpType))
        {
            ModelState.AddModelError(
                nameof(followUp.FollowUpType),
                "Select a valid follow-up type.");
        }

        if (!ValidStatuses.Contains(followUp.Status))
        {
            ModelState.AddModelError(
                nameof(followUp.Status),
                "Select a valid follow-up status.");
        }
    }

    private void ValidateFollowUpDate(FollowUp followUp)
    {
        if (followUp.Status == "Planned" &&
            followUp.FollowUpDate.Date < DateTime.Today)
        {
            ModelState.AddModelError(
                nameof(followUp.FollowUpDate),
                "A planned follow-up cannot be scheduled in the past.");
        }
    }

    private async Task<bool> ValidateLinksAsync(FollowUp followUp)
    {
        if (followUp.CustomerId.HasValue == followUp.LeadId.HasValue)
        {
            ModelState.AddModelError(string.Empty,
                "Select exactly one customer or one lead.");
            return false;
        }

        if (followUp.CustomerId.HasValue)
        {
            var customerQuery = _context.Customers
                .Where(c => c.CustomerId == followUp.CustomerId.Value);

            if (!IsAdmin)
                customerQuery = customerQuery
                    .Where(c => c.CreatedBy == CurrentUserId);

            if (!await customerQuery.AnyAsync())
            {
                ModelState.AddModelError(nameof(followUp.CustomerId),
                    "The selected customer does not exist or is not accessible.");
                return false;
            }
        }

        if (followUp.LeadId.HasValue)
        {
            var leadQuery = _context.Leads
                .Where(l => l.LeadId == followUp.LeadId.Value);

            if (!IsAdmin)
                leadQuery = leadQuery
                    .Where(l => l.AssignedTo == CurrentUserId);

            if (!await leadQuery.AnyAsync())
            {
                ModelState.AddModelError(nameof(followUp.LeadId),
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
            customers = customers.Where(c => c.CreatedBy == CurrentUserId);
            leads = leads.Where(l => l.AssignedTo == CurrentUserId);
        }

        ViewBag.Customers = new SelectList(
            await customers.OrderBy(c => c.CustomerName).ToListAsync(),
            "CustomerId", "CustomerName", selectedCustomerId);

        ViewBag.Leads = new SelectList(
            await leads.OrderBy(l => l.LeadName).ToListAsync(),
            "LeadId", "LeadName", selectedLeadId);

        ViewBag.Types = new SelectList(ValidTypes);
        ViewBag.Statuses = new SelectList(ValidStatuses);

        if (IsAdmin)
        {
            var users = await _userManager.Users
                .Where(u => u.IsActive)
                .OrderBy(u => u.FullName)
                .ToListAsync();

            ViewBag.Users = new SelectList(
                users, "Id", "FullName", selectedUserId);
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
            EntityName = "FollowUp",
            RecordId = recordId,
            OldValue = oldValue,
            NewValue = newValue,
            CreatedDate = DateTime.UtcNow,
            Result = "Success",
            Details = $"{action} follow-up record {recordId}"
        });
    }
}