
using System.Text.Json;
using AcxiomCRM.Data;
using AcxiomCRM.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[Authorize(Roles = "Admin,Manager,SalesExecutive")]
public class OpportunityController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    private static readonly string[] Stages =
    {
        "Qualification", "Proposal", "Negotiation", "Won", "Lost"
    };

    private static readonly string[] Statuses =
    {
        "Open", "Won", "Lost"
    };

    public OpportunityController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    private string CurrentUserId => _userManager.GetUserId(User)!;

    private bool IsAdmin => User.IsInRole("Admin");
    private bool IsManager => User.IsInRole("Manager");

    // Admin: all opportunities.
    // Manager: own opportunities and direct team members' opportunities.
    // SalesExecutive: only assigned opportunities.
    private IQueryable<Opportunity> AccessibleOpportunities()
    {
        var query = _context.Opportunities.AsQueryable();

        if (IsAdmin)
            return query;

        if (IsManager)
        {
            query = query.Where(o =>
                o.AssignedTo == CurrentUserId ||
                _userManager.Users.Any(u =>
                    u.Id == o.AssignedTo &&
                    u.ManagerId == CurrentUserId));
        }
        else
        {
            query = query.Where(o => o.AssignedTo == CurrentUserId);
        }

        return query;
    }

    private IQueryable<Customer> AccessibleCustomers()
    {
        var query = _context.Customers.AsQueryable();

        if (IsAdmin)
            return query;

        if (IsManager)
        {
            query = query.Where(c =>
                c.CreatedBy == CurrentUserId ||
                _userManager.Users.Any(u =>
                    u.Id == c.CreatedBy &&
                    u.ManagerId == CurrentUserId));
        }
        else
        {
            query = query.Where(c => c.CreatedBy == CurrentUserId);
        }

        return query;
    }

    private IQueryable<Lead> AccessibleLeads()
    {
        var query = _context.Leads.AsQueryable();

        if (IsAdmin)
            return query;

        if (IsManager)
        {
            query = query.Where(l =>
                l.AssignedTo == CurrentUserId ||
                _userManager.Users.Any(u =>
                    u.Id == l.AssignedTo &&
                    u.ManagerId == CurrentUserId));
        }
        else
        {
            query = query.Where(l => l.AssignedTo == CurrentUserId);
        }

        return query;
    }

    private void AddAudit(
        string action,
        int recordId,
        string? oldValue = null,
        string? newValue = null)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = CurrentUserId,
            Action = action,
            EntityName = "Opportunity",
            RecordId = recordId,
            OldValue = oldValue,
            NewValue = newValue,
            CreatedDate = DateTime.UtcNow,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Result = "Success"
        });
    }

    private async Task LoadFormDataAsync(
        string? selectedUser = null,
        int? selectedCustomer = null,
        int? selectedLead = null)
    {
        ViewBag.Stages = Stages;
        ViewBag.Statuses = Statuses;

        ViewBag.Customers = await AccessibleCustomers()
            .OrderBy(c => c.CustomerName)
            .ToListAsync();

        ViewBag.Leads = await AccessibleLeads()
            .Where(l => l.Status != "Converted" && l.Status != "Lost")
            .OrderBy(l => l.LeadName)
            .ToListAsync();

        ViewBag.SelectedCustomer = selectedCustomer;
        ViewBag.SelectedLead = selectedLead;

        if (IsAdmin)
        {
            ViewBag.Users = await _userManager.Users
                .Where(u => u.IsActive)
                .OrderBy(u => u.FullName)
                .ToListAsync();
        }
        else if (IsManager)
        {
            ViewBag.Users = await _userManager.Users
                .Where(u => u.IsActive &&
                    (u.Id == CurrentUserId ||
                     u.ManagerId == CurrentUserId))
                .OrderBy(u => u.FullName)
                .ToListAsync();
        }

        if (IsAdmin || IsManager)
            ViewBag.SelectedUser = selectedUser;
    }

    private bool IsValidStageAndStatus(Opportunity opportunity)
    {
        if (!Stages.Contains(opportunity.Stage) ||
            !Statuses.Contains(opportunity.Status))
        {
            return false;
        }

        if (opportunity.Stage == "Won")
            return opportunity.Status == "Won";

        if (opportunity.Stage == "Lost")
            return opportunity.Status == "Lost";

        return opportunity.Status == "Open";
    }

    private async Task<bool> ValidateRelationshipsAsync(
        Opportunity opportunity)
    {
        if (opportunity.CustomerId == null &&
            opportunity.LeadId == null)
        {
            ModelState.AddModelError(
                "",
                "Select a customer or a lead.");

            return false;
        }

        if (opportunity.CustomerId.HasValue &&
            opportunity.LeadId.HasValue)
        {
            ModelState.AddModelError(
                "",
                "An opportunity must be linked to either a customer or a lead, not both.");

            return false;
        }

        if (opportunity.CustomerId.HasValue)
        {
            var customerExists = await AccessibleCustomers()
                .AnyAsync(c => c.CustomerId == opportunity.CustomerId);

            if (!customerExists)
            {
                ModelState.AddModelError(
                    nameof(Opportunity.CustomerId),
                    "Select a valid accessible customer.");

                return false;
            }
        }

        if (opportunity.LeadId.HasValue)
        {
            var leadExists = await AccessibleLeads()
                .AnyAsync(l =>
                    l.LeadId == opportunity.LeadId &&
                    l.Status != "Converted" &&
                    l.Status != "Lost");

            if (!leadExists)
            {
                ModelState.AddModelError(
                    nameof(Opportunity.LeadId),
                    "Select a valid accessible lead.");

                return false;
            }
        }

        return true;
    }

    private async Task<bool> ValidateAssignmentAsync(string? assignedTo)
    {
        if (string.IsNullOrWhiteSpace(assignedTo))
            return false;

        var user = await _userManager.Users
            .FirstOrDefaultAsync(u =>
                u.Id == assignedTo && u.IsActive);

        if (user == null)
            return false;

        if (IsAdmin)
            return true;

        if (IsManager)
        {
            return user.Id == CurrentUserId ||
                   user.ManagerId == CurrentUserId;
        }

        return user.Id == CurrentUserId;
    }

    // GET: /Opportunity
    public async Task<IActionResult> Index(
        string? search,
        string? stage,
        string? status,
        int page = 1)
    {
        const int pageSize = 10;

        var query = AccessibleOpportunities()
            .Include(o => o.Customer)
            .Include(o => o.Lead)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(o =>
                o.OpportunityName.Contains(search) ||
                (o.Customer != null &&
                 o.Customer.CustomerName.Contains(search)) ||
                (o.Lead != null &&
                 o.Lead.LeadName.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(stage))
            query = query.Where(o => o.Stage == stage);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(o => o.Status == status);

        var totalRecords = await query.CountAsync();

        page = Math.Max(page, 1);

        var opportunities = await query
            .OrderByDescending(o => o.CreatedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Search = search;
        ViewBag.Stage = stage;
        ViewBag.Status = status;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages =
            (int)Math.Ceiling(totalRecords / (double)pageSize);

        ViewBag.Stages = Stages;
        ViewBag.Statuses = Statuses;

        return View(opportunities);
    }

    // GET: /Opportunity/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var opportunity = await AccessibleOpportunities()
            .Include(o => o.Customer)
            .Include(o => o.Lead)
            .FirstOrDefaultAsync(o => o.OpportunityId == id);

        if (opportunity == null)
            return NotFound();

        var user = await _userManager.FindByIdAsync(
            opportunity.AssignedTo);

        ViewBag.AssignedUser = user?.FullName ?? "Unknown";

        return View(opportunity);
    }

    // GET: /Opportunity/Create
    public async Task<IActionResult> Create()
    {
        await LoadFormDataAsync(CurrentUserId);

        return View(new Opportunity
        {
            AssignedTo = CurrentUserId,
            Stage = "Qualification",
            Status = "Open",
            Probability = 10,
            ExpectedCloseDate = DateTime.Today.AddDays(30)
        });
    }

    // POST: /Opportunity/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Opportunity opportunity)
    {
        ModelState.Remove(nameof(Opportunity.AssignedTo));
        ModelState.Remove(nameof(Opportunity.CreatedDate));
        ModelState.Remove(nameof(Opportunity.WeightedPipeline));

        // SalesExecutives cannot assign opportunities to other users.
        if (!IsAdmin && !IsManager)
        {
            opportunity.AssignedTo = CurrentUserId;
        }
        else if (string.IsNullOrWhiteSpace(opportunity.AssignedTo))
        {
            opportunity.AssignedTo = CurrentUserId;
        }

        await ValidateRelationshipsAsync(opportunity);

        if ((IsAdmin || IsManager) &&
            !await ValidateAssignmentAsync(opportunity.AssignedTo))
        {
            ModelState.AddModelError(
                nameof(Opportunity.AssignedTo),
                "Select a valid active user within your permitted assignment scope.");
        }

        if (!IsValidStageAndStatus(opportunity))
        {
            ModelState.AddModelError(
                nameof(Opportunity.Stage),
                "The selected stage and status combination is invalid.");
        }

        if (opportunity.Status == "Open" &&
            opportunity.ExpectedCloseDate.Date < DateTime.Today)
        {
            ModelState.AddModelError(
                nameof(Opportunity.ExpectedCloseDate),
                "Expected close date cannot be in the past for an open opportunity.");
        }

        if (!ModelState.IsValid)
        {
            await LoadFormDataAsync(
                opportunity.AssignedTo,
                opportunity.CustomerId,
                opportunity.LeadId);

            return View(opportunity);
        }

        opportunity.CreatedDate = DateTime.UtcNow;

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            _context.Opportunities.Add(opportunity);
            await _context.SaveChangesAsync();

            AddAudit(
                "Create",
                opportunity.OpportunityId,
                newValue: JsonSerializer.Serialize(opportunity));

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = "Opportunity created successfully.";

            return RedirectToAction(nameof(Index));
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // GET: /Opportunity/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var opportunity = await AccessibleOpportunities()
            .FirstOrDefaultAsync(o => o.OpportunityId == id);

        if (opportunity == null)
            return NotFound();

        await LoadFormDataAsync(
            opportunity.AssignedTo,
            opportunity.CustomerId,
            opportunity.LeadId);

        return View(opportunity);
    }

    // POST: /Opportunity/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Opportunity input)
    {
        if (id != input.OpportunityId)
            return BadRequest();

        var opportunity = await AccessibleOpportunities()
            .FirstOrDefaultAsync(o => o.OpportunityId == id);

        if (opportunity == null)
            return NotFound();

        ModelState.Remove(nameof(Opportunity.AssignedTo));
        ModelState.Remove(nameof(Opportunity.CreatedDate));
        ModelState.Remove(nameof(Opportunity.WeightedPipeline));

        // SalesExecutives retain the existing owner.
        if (!IsAdmin && !IsManager)
        {
            input.AssignedTo = opportunity.AssignedTo;
        }
        else if (string.IsNullOrWhiteSpace(input.AssignedTo))
        {
            input.AssignedTo = opportunity.AssignedTo;
        }

        await ValidateRelationshipsAsync(input);

        if ((IsAdmin || IsManager) &&
            !await ValidateAssignmentAsync(input.AssignedTo))
        {
            ModelState.AddModelError(
                nameof(Opportunity.AssignedTo),
                "Select a valid active user within your permitted assignment scope.");
        }

        if (!IsValidStageAndStatus(input))
        {
            ModelState.AddModelError(
                nameof(Opportunity.Stage),
                "The selected stage and status combination is invalid.");
        }

        if (input.Status == "Open" &&
            input.ExpectedCloseDate.Date < DateTime.Today)
        {
            ModelState.AddModelError(
                nameof(Opportunity.ExpectedCloseDate),
                "Expected close date cannot be in the past for an open opportunity.");
        }

        if (!ModelState.IsValid)
        {
            await LoadFormDataAsync(
                input.AssignedTo,
                input.CustomerId,
                input.LeadId);

            return View(input);
        }

        var oldValue = JsonSerializer.Serialize(opportunity);

        opportunity.OpportunityName = input.OpportunityName;
        opportunity.CustomerId = input.CustomerId;
        opportunity.LeadId = input.LeadId;
        opportunity.Amount = input.Amount;
        opportunity.Stage = input.Stage;
        opportunity.Probability = input.Probability;
        opportunity.ExpectedCloseDate = input.ExpectedCloseDate;
        opportunity.Status = input.Status;
        opportunity.Notes = input.Notes;

        if (IsAdmin || IsManager)
            opportunity.AssignedTo = input.AssignedTo;

        var newValue = JsonSerializer.Serialize(opportunity);

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            AddAudit(
                "Update",
                opportunity.OpportunityId,
                oldValue,
                newValue);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = "Opportunity updated successfully.";

            return RedirectToAction(nameof(Index));
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // GET: /Opportunity/Delete/5
    public async Task<IActionResult> Delete(int id)
    {
        var opportunity = await AccessibleOpportunities()
            .Include(o => o.Customer)
            .Include(o => o.Lead)
            .FirstOrDefaultAsync(o => o.OpportunityId == id);

        if (opportunity == null)
            return NotFound();

        return View(opportunity);
    }

    // POST: /Opportunity/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var opportunity = await AccessibleOpportunities()
            .FirstOrDefaultAsync(o => o.OpportunityId == id);

        if (opportunity == null)
            return NotFound();

        var oldValue = JsonSerializer.Serialize(opportunity);

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            AddAudit(
                "Delete",
                opportunity.OpportunityId,
                oldValue);

            _context.Opportunities.Remove(opportunity);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = "Opportunity deleted successfully.";

            return RedirectToAction(nameof(Index));
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
