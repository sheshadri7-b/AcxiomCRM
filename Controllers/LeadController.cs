
using System.Text.Json;
using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[Authorize(Roles = "Admin,Manager,SalesExecutive")]
public class LeadController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    private static readonly string[] LeadStatuses =
    {
        "New", "Contacted", "Qualified",
        "Unqualified", "Converted", "Lost"
    };

    private static readonly string[] Priorities =
    {
        "Low", "Medium", "High"
    };

    public LeadController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    private string CurrentUserId =>
        _userManager.GetUserId(User)!;

    private bool IsAdmin => User.IsInRole("Admin");

    private bool IsManager => User.IsInRole("Manager");

    private bool CanAssignUsers => IsAdmin || IsManager;

    // Admin: all leads.
    // Manager: own leads and direct team members' leads.
    // SalesExecutive: only their own assigned leads.
    private IQueryable<Lead> AccessibleLeads()
    {
        var query = _context.Leads.AsQueryable();

        if (IsAdmin)
            return query;

        if (IsManager)
        {
            return query.Where(l =>
                l.AssignedTo == CurrentUserId ||
                _userManager.Users.Any(u =>
                    u.Id == l.AssignedTo &&
                    u.ManagerId == CurrentUserId));
        }

        return query.Where(l => l.AssignedTo == CurrentUserId);
    }

    // Users to whom the current user is allowed to assign leads.
    private IQueryable<ApplicationUser> AssignableUsers()
    {
        var users = _userManager.Users
            .Where(u => u.IsActive);

        if (IsAdmin)
            return users;

        if (IsManager)
        {
            return users.Where(u =>
                u.Id == CurrentUserId ||
                u.ManagerId == CurrentUserId);
        }

        return users.Where(u => u.Id == CurrentUserId);
    }

    private async Task<bool> CanAssignLeadToAsync(string? assignedTo)
    {
        if (string.IsNullOrWhiteSpace(assignedTo))
            return false;

        return await AssignableUsers()
            .AnyAsync(u => u.Id == assignedTo);
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
            EntityName = "Lead",
            RecordId = recordId,
            OldValue = oldValue,
            NewValue = newValue,
            CreatedDate = DateTime.UtcNow,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Result = "Success"
        });
    }

    private static object LeadAuditSnapshot(Lead lead)
    {
        return new
        {
            lead.LeadId,
            lead.LeadCode,
            lead.LeadName,
            lead.Email,
            lead.Phone,
            lead.CompanyName,
            lead.Source,
            lead.Status,
            lead.Priority,
            lead.ExpectedValue,
            lead.AssignedTo,
            lead.CreatedDate,
            lead.Notes,
            lead.ConvertedCustomerId
        };
    }

    private async Task LoadUsersAsync(string? selectedUser = null)
    {
        var users = await AssignableUsers()
            .OrderBy(u => u.FullName)
            .ToListAsync();

        ViewBag.Users = users;
        ViewBag.SelectedUser = selectedUser;
    }

    private void LoadLeadOptions()
    {
        ViewBag.Statuses = LeadStatuses;
        ViewBag.Priorities = Priorities;
    }

    // GET: /Lead
    public async Task<IActionResult> Index(
        string? search,
        string? status,
        string? priority,
        int page = 1)
    {
        const int pageSize = 10;

        var query = AccessibleLeads();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();

            query = query.Where(l =>
                l.LeadName.Contains(search) ||
                l.LeadCode.Contains(search) ||
                (l.Email != null && l.Email.Contains(search)) ||
                (l.Phone != null && l.Phone.Contains(search)) ||
                (l.CompanyName != null &&
                 l.CompanyName.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(status) &&
            LeadStatuses.Contains(status))
        {
            query = query.Where(l => l.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(priority) &&
            Priorities.Contains(priority))
        {
            query = query.Where(l => l.Priority == priority);
        }

        var totalRecords = await query.CountAsync();

        page = Math.Max(page, 1);

        var totalPages = (int)Math.Ceiling(
            totalRecords / (double)pageSize);

        if (totalPages > 0)
            page = Math.Min(page, totalPages);

        var leads = await query
            .OrderByDescending(l => l.CreatedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Search = search;
        ViewBag.Status = status;
        ViewBag.Priority = priority;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;

        LoadLeadOptions();

        return View(leads);
    }

    // GET: /Lead/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var lead = await AccessibleLeads()
            .FirstOrDefaultAsync(l => l.LeadId == id);

        if (lead == null)
            return NotFound();

        var assignedUser = string.IsNullOrWhiteSpace(lead.AssignedTo)
            ? null
            : await _userManager.FindByIdAsync(lead.AssignedTo);

        ViewBag.AssignedUser = assignedUser?.FullName ?? "Unknown";

        return View(lead);
    }

    // GET: /Lead/Create
    public async Task<IActionResult> Create()
    {
        LoadLeadOptions();

        if (CanAssignUsers)
            await LoadUsersAsync(CurrentUserId);

        return View();
    }

    // POST: /Lead/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Lead lead)
    {
        ModelState.Remove(nameof(Lead.LeadCode));
        ModelState.Remove(nameof(Lead.AssignedTo));
        ModelState.Remove(nameof(Lead.CreatedDate));
        ModelState.Remove(nameof(Lead.ConvertedCustomerId));

        // SalesExecutives cannot choose another owner.
        if (!CanAssignUsers)
        {
            lead.AssignedTo = CurrentUserId;
        }
        else if (!await CanAssignLeadToAsync(lead.AssignedTo))
        {
            ModelState.AddModelError(
                nameof(Lead.AssignedTo),
                "You cannot assign leads to this user.");
        }

        if (!LeadStatuses.Contains(lead.Status))
        {
            ModelState.AddModelError(
                nameof(Lead.Status),
                "Invalid lead status.");
        }

        if (!Priorities.Contains(lead.Priority))
        {
            ModelState.AddModelError(
                nameof(Lead.Priority),
                "Invalid lead priority.");
        }

        if (lead.Status == "Converted")
        {
            ModelState.AddModelError(
                nameof(Lead.Status),
                "Use the lead conversion workflow to convert a lead.");
        }

        if (!ModelState.IsValid)
        {
            LoadLeadOptions();

            if (CanAssignUsers)
                await LoadUsersAsync(lead.AssignedTo);

            return View(lead);
        }

        lead.LeadCode =
            $"LEAD-{Guid.NewGuid():N}"[..13].ToUpperInvariant();

        lead.CreatedDate = DateTime.UtcNow;
        lead.Email = lead.Email?.Trim().ToLowerInvariant();
        lead.Phone = lead.Phone?.Trim();

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            _context.Leads.Add(lead);
            await _context.SaveChangesAsync();

            AddAudit(
                "Create",
                lead.LeadId,
                newValue: JsonSerializer.Serialize(
                    LeadAuditSnapshot(lead)));

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = "Lead created successfully.";

            return RedirectToAction(nameof(Index));
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // GET: /Lead/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var lead = await AccessibleLeads()
            .FirstOrDefaultAsync(l => l.LeadId == id);

        if (lead == null)
            return NotFound();

        LoadLeadOptions();

        if (CanAssignUsers)
            await LoadUsersAsync(lead.AssignedTo);

        return View(lead);
    }

    // POST: /Lead/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Lead input)
    {
        if (id != input.LeadId)
            return BadRequest();

        var lead = await AccessibleLeads()
            .FirstOrDefaultAsync(l => l.LeadId == id);

        if (lead == null)
            return NotFound();

        ModelState.Remove(nameof(Lead.LeadCode));
        ModelState.Remove(nameof(Lead.AssignedTo));
        ModelState.Remove(nameof(Lead.CreatedDate));
        ModelState.Remove(nameof(Lead.FollowUps));
        ModelState.Remove(nameof(Lead.Activities));
        ModelState.Remove(nameof(Lead.ConvertedCustomerId));

        if (!LeadStatuses.Contains(input.Status))
        {
            ModelState.AddModelError(
                nameof(Lead.Status),
                "Invalid lead status.");
        }

        if (!Priorities.Contains(input.Priority))
        {
            ModelState.AddModelError(
                nameof(Lead.Priority),
                "Invalid lead priority.");
        }

        if (input.Status == "Converted" &&
            lead.Status != "Converted")
        {
            ModelState.AddModelError(
                nameof(Lead.Status),
                "Use the lead conversion workflow to convert a lead.");
        }

        if (CanAssignUsers &&
            !await CanAssignLeadToAsync(input.AssignedTo))
        {
            ModelState.AddModelError(
                nameof(Lead.AssignedTo),
                "You cannot assign leads to this user.");
        }

        if (!ModelState.IsValid)
        {
            LoadLeadOptions();

            if (CanAssignUsers)
                await LoadUsersAsync(input.AssignedTo ?? lead.AssignedTo);

            return View(input);
        }

        // Capture the original values before changing anything.
        var oldValue = JsonSerializer.Serialize(
            LeadAuditSnapshot(lead));

        // Only Admins and Managers can change lead ownership.
        if (CanAssignUsers)
            lead.AssignedTo = input.AssignedTo;

        lead.LeadName = input.LeadName;
        lead.Email = input.Email?.Trim().ToLowerInvariant();
        lead.Phone = input.Phone?.Trim();
        lead.CompanyName = input.CompanyName;
        lead.Source = input.Source;
        lead.Status = input.Status;
        lead.Priority = input.Priority;
        lead.ExpectedValue = input.ExpectedValue;
        lead.Notes = input.Notes;

        var newValue = JsonSerializer.Serialize(
            LeadAuditSnapshot(lead));

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            AddAudit(
                "Update",
                lead.LeadId,
                oldValue,
                newValue);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = "Lead updated successfully.";

            return RedirectToAction(nameof(Index));
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // GET: /Lead/Delete/5
    public async Task<IActionResult> Delete(int id)
    {
        var lead = await AccessibleLeads()
            .FirstOrDefaultAsync(l => l.LeadId == id);

        if (lead == null)
            return NotFound();

        return View(lead);
    }

    // POST: /Lead/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var lead = await AccessibleLeads()
            .FirstOrDefaultAsync(l => l.LeadId == id);

        if (lead == null)
            return NotFound();

        bool hasOpportunities = await _context.Opportunities
            .AnyAsync(o => o.LeadId == id);

        bool hasFollowUps = await _context.FollowUps
            .AnyAsync(f => f.LeadId == id);

        bool hasActivities = await _context.Activities
            .AnyAsync(a => a.LeadId == id);

        if (hasOpportunities || hasFollowUps || hasActivities)
        {
            TempData["Error"] =
                "This lead cannot be deleted because it has related opportunities, follow-ups or activities.";

            return RedirectToAction(nameof(Index));
        }

        var oldValue = JsonSerializer.Serialize(
            LeadAuditSnapshot(lead));

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            AddAudit(
                "Delete",
                lead.LeadId,
                oldValue);

            _context.Leads.Remove(lead);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = "Lead deleted successfully.";

            return RedirectToAction(nameof(Index));
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // GET: /Lead/Convert/5
    public async Task<IActionResult> Convert(int id)
    {
        var lead = await AccessibleLeads()
            .FirstOrDefaultAsync(l => l.LeadId == id);

        if (lead == null)
            return NotFound();

        if (lead.Status != "Qualified")
        {
            TempData["Error"] =
                "Only Qualified leads can be converted.";

            return RedirectToAction(nameof(Details), new { id });
        }

        if (string.IsNullOrWhiteSpace(lead.Email) ||
            string.IsNullOrWhiteSpace(lead.Phone))
        {
            TempData["Error"] =
                "This lead needs both an email address and phone number before conversion.";

            return RedirectToAction(nameof(Edit), new { id });
        }

        ViewBag.Lead = lead;

        var model = new LeadConversionViewModel
        {
            LeadId = lead.LeadId,
            OpportunityName =
                $"{lead.CompanyName ?? lead.LeadName} Opportunity",
            Amount = lead.ExpectedValue > 0
                ? lead.ExpectedValue
                : 1,
            ExpectedCloseDate = DateTime.Today.AddDays(30)
        };

        return View(model);
    }

    // POST: /Lead/Convert/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Convert(
        int id,
        LeadConversionViewModel input)
    {
        if (id != input.LeadId)
            return BadRequest();

        var lead = await AccessibleLeads()
            .FirstOrDefaultAsync(l => l.LeadId == id);

        if (lead == null)
            return NotFound();

        if (lead.Status != "Qualified")
        {
            TempData["Error"] =
                "Only Qualified leads can be converted.";

            return RedirectToAction(nameof(Details), new { id });
        }

        if (string.IsNullOrWhiteSpace(lead.Email) ||
            string.IsNullOrWhiteSpace(lead.Phone))
        {
            ModelState.AddModelError(
                "",
                "The lead must have both an email address and phone number.");
        }

        if (input.ExpectedCloseDate.Date < DateTime.Today)
        {
            ModelState.AddModelError(
                nameof(input.ExpectedCloseDate),
                "Expected close date cannot be in the past.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Lead = lead;
            return View(input);
        }

        var email = lead.Email!.Trim().ToLowerInvariant();
        var phone = lead.Phone!.Trim();

        bool emailExists = await _context.Customers
            .AnyAsync(c => c.Email.ToLower() == email);

        bool phoneExists = await _context.Customers
            .AnyAsync(c => c.Phone == phone);

        if (emailExists || phoneExists)
        {
            ModelState.AddModelError(
                "",
                "A customer with this email address or phone number already exists.");

            ViewBag.Lead = lead;
            return View(input);
        }

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            var oldLeadValue = JsonSerializer.Serialize(
                LeadAuditSnapshot(lead));

            // 1. Create customer from lead information.
            var customer = new Customer
            {
                CustomerCode =
                    $"CUS-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),

                CustomerName = lead.LeadName,
                Email = email,
                Phone = phone,
                CompanyName = lead.CompanyName,
                Status = "Active",
                CreatedBy = CurrentUserId,
                CreatedDate = DateTime.UtcNow,
                AssignedTo = lead.AssignedTo
            };

            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();

            // 2. Create opportunity linked to customer and lead.
            var opportunity = new Opportunity
            {
                OpportunityName = input.OpportunityName.Trim(),
                CustomerId = customer.CustomerId,
                LeadId = lead.LeadId,
                Amount = input.Amount,
                Stage = "Qualification",
                Probability = input.Probability,
                ExpectedCloseDate = input.ExpectedCloseDate,
                Status = "Open",
                AssignedTo = lead.AssignedTo,
                CreatedDate = DateTime.UtcNow,
                Notes = input.Notes
            };

            _context.Opportunities.Add(opportunity);
            await _context.SaveChangesAsync();

            // 3. Mark lead as converted.
            lead.Status = "Converted";
            lead.ConvertedCustomerId = customer.CustomerId;

            var newLeadValue = JsonSerializer.Serialize(
                LeadAuditSnapshot(lead));

            // 4. Audit all three operations.
            _context.AuditLogs.AddRange(
                new AuditLog
                {
                    UserId = CurrentUserId,
                    Action = "Create",
                    EntityName = "Customer",
                    RecordId = customer.CustomerId,
                    NewValue = JsonSerializer.Serialize(new
                    {
                        customer.CustomerId,
                        customer.CustomerCode,
                        customer.CustomerName,
                        customer.Email,
                        customer.Phone,
                        customer.CompanyName,
                        customer.Status,
                        customer.CreatedBy,
                        customer.CreatedDate
                    }),
                    CreatedDate = DateTime.UtcNow,
                    IpAddress = HttpContext.Connection
                        .RemoteIpAddress?.ToString(),
                    Result = "Success"
                },
                new AuditLog
                {
                    UserId = CurrentUserId,
                    Action = "Create",
                    EntityName = "Opportunity",
                    RecordId = opportunity.OpportunityId,
                    NewValue = JsonSerializer.Serialize(new
                    {
                        opportunity.OpportunityId,
                        opportunity.OpportunityName,
                        opportunity.CustomerId,
                        opportunity.LeadId,
                        opportunity.Amount,
                        opportunity.Stage,
                        opportunity.Probability,
                        opportunity.ExpectedCloseDate,
                        opportunity.Status,
                        opportunity.AssignedTo,
                        opportunity.CreatedDate,
                        opportunity.Notes
                    }),
                    CreatedDate = DateTime.UtcNow,
                    IpAddress = HttpContext.Connection
                        .RemoteIpAddress?.ToString(),
                    Result = "Success"
                },
                new AuditLog
                {
                    UserId = CurrentUserId,
                    Action = "Update",
                    EntityName = "Lead",
                    RecordId = lead.LeadId,
                    OldValue = oldLeadValue,
                    NewValue = newLeadValue,
                    CreatedDate = DateTime.UtcNow,
                    IpAddress = HttpContext.Connection
                        .RemoteIpAddress?.ToString(),
                    Result = "Success"
                }
            );

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] =
                "Lead converted successfully. Customer and opportunity created.";

            return RedirectToAction(
                "Details",
                "Customer",
                new { id = customer.CustomerId });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
