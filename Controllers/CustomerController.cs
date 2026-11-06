using System.Text.Json;
using AcxiomCRM.Data;
using AcxiomCRM.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[Authorize(Roles = "Admin,Manager,SalesExecutive")]
public class CustomerController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public CustomerController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    private bool CanManageAssignments =>
        User.IsInRole("Admin") || User.IsInRole("Manager");

    private async Task<string> CurrentUserIdAsync()
    {
        var user = await _userManager.GetUserAsync(User)
            ?? throw new InvalidOperationException("User not found.");

        return user.Id;
    }

    // Admins can see all customers. Managers can see their own customers,
    // customers assigned to themselves, and customers assigned to direct reports.
    // Sales executives can see customers they created or that are assigned to them.
    private async Task<IQueryable<Customer>> AccessibleCustomersAsync(string userId)
    {
        var query = _context.Customers.AsQueryable();

        if (User.IsInRole("Admin"))
            return query;

        if (User.IsInRole("Manager"))
        {
            var teamMemberIds = await _context.Users
                .Where(u => u.ManagerId == userId)
                .Select(u => u.Id)
                .ToListAsync();

            return query.Where(c =>
                c.CreatedBy == userId ||
                c.AssignedTo == userId ||
                (c.AssignedTo != null && teamMemberIds.Contains(c.AssignedTo)));
        }

        return query.Where(c =>
            c.CreatedBy == userId || c.AssignedTo == userId);
    }

    private async Task<List<ApplicationUser>> GetAssignableUsersAsync(string currentUserId)
    {
        if (User.IsInRole("Admin"))
        {
            var salesExecutives = await _userManager.GetUsersInRoleAsync("SalesExecutive");
            var managers = await _userManager.GetUsersInRoleAsync("Manager");

            return salesExecutives
                .Concat(managers)
                .Where(u => u.IsActive)
                .DistinctBy(u => u.Id)
                .OrderBy(u => u.FullName)
                .ToList();
        }

        if (User.IsInRole("Manager"))
        {
            return await _context.Users
                .Where(u => u.ManagerId == currentUserId && u.IsActive)
                .OrderBy(u => u.FullName)
                .ToListAsync();
        }

        return new List<ApplicationUser>();
    }

    private async Task LoadAssignableUsersAsync(
        string? selectedUserId = null)
    {
        ViewBag.CanManageAssignments = CanManageAssignments;

        if (!CanManageAssignments)
        {
            ViewBag.AssignableUsers = new List<SelectListItem>();
            return;
        }

        var currentUserId = await CurrentUserIdAsync();
        var users = await GetAssignableUsersAsync(currentUserId);

        ViewBag.AssignableUsers = users
            .Select(u => new SelectListItem
            {
                Value = u.Id,
                Text = $"{u.FullName} ({u.Email})",
                Selected = u.Id == selectedUserId
            })
            .Prepend(new SelectListItem
            {
                Value = "",
                Text = "Unassigned",
                Selected = string.IsNullOrEmpty(selectedUserId)
            })
            .ToList();
    }

    private void AddAudit(
        string action,
        int recordId,
        string? oldValue,
        string? newValue)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = action,
            EntityName = "Customer",
            RecordId = recordId,
            OldValue = oldValue,
            NewValue = newValue,
            CreatedDate = DateTime.UtcNow,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Result = "Success"
        });
    }

    private async Task<bool> IsValidAssignmentAsync(
        string? assignedTo,
        string currentUserId)
    {
        if (string.IsNullOrWhiteSpace(assignedTo))
            return true;

        var allowedUsers = await GetAssignableUsersAsync(currentUserId);
        return allowedUsers.Any(u => u.Id == assignedTo);
    }

    // GET: /Customer
    public async Task<IActionResult> Index(
        string? search,
        string? status,
        int page = 1)
    {
        const int pageSize = 10;

        if (page < 1)
            page = 1;

        var userId = await CurrentUserIdAsync();
        var query = await AccessibleCustomersAsync(userId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();

            query = query.Where(c =>
                c.CustomerName.Contains(search) ||
                c.Email.Contains(search) ||
                c.Phone.Contains(search) ||
                (c.CompanyName != null && c.CompanyName.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(c => c.Status == status);

        var totalRecords = await query.CountAsync();

        var customers = await query
            .AsNoTracking()
            .Include(c => c.AssignedUser)
            .OrderByDescending(c => c.CreatedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Search = search;
        ViewBag.Status = status;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);
        ViewBag.TotalRecords = totalRecords;

        return View(customers);
    }

    // GET: /Customer/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var userId = await CurrentUserIdAsync();
        var query = await AccessibleCustomersAsync(userId);

        var customer = await query
            .AsNoTracking()
            .Include(c => c.AssignedUser)
            .FirstOrDefaultAsync(c => c.CustomerId == id);

        if (customer == null)
            return NotFound();

        return View(customer);
    }

    // GET: /Customer/Create
    public async Task<IActionResult> Create()
    {
        var customer = new Customer();

        // Sales executives automatically own customers they create.
        if (!CanManageAssignments)
            customer.AssignedTo = await CurrentUserIdAsync();

        await LoadAssignableUsersAsync(customer.AssignedTo);
        return View(customer);
    }

    // POST: /Customer/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Customer customer)
    {
        ModelState.Remove(nameof(Customer.CustomerCode));
        ModelState.Remove(nameof(Customer.CreatedBy));
        ModelState.Remove(nameof(Customer.CreatedDate));
        ModelState.Remove(nameof(Customer.ModifiedDate));
        ModelState.Remove(nameof(Customer.AssignedUser));

        var userId = await CurrentUserIdAsync();

        if (CanManageAssignments)
        {
            if (!await IsValidAssignmentAsync(customer.AssignedTo, userId))
            {
                ModelState.AddModelError(
                    nameof(Customer.AssignedTo),
                    "Select an active employee you are authorized to assign.");
            }
        }
        else
        {
            // Never trust a Sales Executive-supplied assignment value.
            customer.AssignedTo = userId;
        }

        if (!ModelState.IsValid)
        {
            await LoadAssignableUsersAsync(customer.AssignedTo);
            return View(customer);
        }

        customer.CustomerCode =
            $"CUST-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";

        customer.Email = customer.Email.Trim().ToLowerInvariant();
        customer.Phone = customer.Phone.Trim();
        customer.CustomerName = customer.CustomerName.Trim();
        customer.CreatedBy = userId;
        customer.CreatedDate = DateTime.UtcNow;
        customer.Status = "Active";
        customer.AssignedUser = null;

        var duplicateEmail = await _context.Customers
            .AnyAsync(c => c.Email.ToLower() == customer.Email);

        var duplicatePhone = await _context.Customers
            .AnyAsync(c => c.Phone == customer.Phone);

        if (duplicateEmail)
            ModelState.AddModelError(nameof(customer.Email), "This email already exists.");

        if (duplicatePhone)
            ModelState.AddModelError(nameof(customer.Phone), "This phone number already exists.");

        if (!ModelState.IsValid)
        {
            await LoadAssignableUsersAsync(customer.AssignedTo);
            return View(customer);
        }

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        AddAudit("Create", customer.CustomerId, null, JsonSerializer.Serialize(customer));
        await _context.SaveChangesAsync();

        TempData["Success"] = "Customer created successfully.";
        return RedirectToAction(nameof(Index));
    }

    // GET: /Customer/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var userId = await CurrentUserIdAsync();
        var query = await AccessibleCustomersAsync(userId);

        var customer = await query
            .Include(c => c.AssignedUser)
            .FirstOrDefaultAsync(c => c.CustomerId == id);

        if (customer == null)
            return NotFound();

        await LoadAssignableUsersAsync(customer.AssignedTo);
        return View(customer);
    }

    // POST: /Customer/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Customer input)
    {
        if (id != input.CustomerId)
            return BadRequest();

        var userId = await CurrentUserIdAsync();
        var query = await AccessibleCustomersAsync(userId);

        var customer = await query
            .FirstOrDefaultAsync(c => c.CustomerId == id);

        if (customer == null)
            return NotFound();

        ModelState.Remove(nameof(Customer.CustomerCode));
        ModelState.Remove(nameof(Customer.CreatedBy));
        ModelState.Remove(nameof(Customer.CreatedDate));
        ModelState.Remove(nameof(Customer.ModifiedDate));
        ModelState.Remove(nameof(Customer.AssignedUser));
        ModelState.Remove(nameof(Customer.Opportunities));
        ModelState.Remove(nameof(Customer.FollowUps));
        ModelState.Remove(nameof(Customer.Activities));

        if (CanManageAssignments)
        {
            if (!await IsValidAssignmentAsync(input.AssignedTo, userId))
            {
                ModelState.AddModelError(
                    nameof(Customer.AssignedTo),
                    "Select an active employee you are authorized to assign.");
            }
        }
        else
        {
            // Sales Executives cannot transfer or clear customer assignments.
            ModelState.Remove(nameof(Customer.AssignedTo));
            input.AssignedTo = customer.AssignedTo;
        }

        if (!ModelState.IsValid)
        {
            await LoadAssignableUsersAsync(customer.AssignedTo);
            input.CustomerCode = customer.CustomerCode;
            return View(input);
        }

        var email = input.Email.Trim().ToLowerInvariant();
        var phone = input.Phone.Trim();

        var duplicateEmail = await _context.Customers
            .AnyAsync(c => c.CustomerId != id && c.Email.ToLower() == email);

        var duplicatePhone = await _context.Customers
            .AnyAsync(c => c.CustomerId != id && c.Phone == phone);

        if (duplicateEmail)
            ModelState.AddModelError(nameof(input.Email), "This email already exists.");

        if (duplicatePhone)
            ModelState.AddModelError(nameof(input.Phone), "This phone number already exists.");

        if (!ModelState.IsValid)
        {
            await LoadAssignableUsersAsync(customer.AssignedTo);
            input.CustomerCode = customer.CustomerCode;
            return View(input);
        }

        var oldValue = JsonSerializer.Serialize(customer);

        customer.CustomerName = input.CustomerName.Trim();
        customer.Email = email;
        customer.Phone = phone;
        customer.CompanyName = input.CompanyName;
        customer.Address = input.Address;
        customer.City = input.City;
        customer.State = input.State;
        customer.Status = input.Status;

        if (CanManageAssignments)
            customer.AssignedTo = input.AssignedTo;

        customer.ModifiedDate = DateTime.UtcNow;

        var newValue = JsonSerializer.Serialize(customer);
        AddAudit("Update", customer.CustomerId, oldValue, newValue);

        await _context.SaveChangesAsync();

        TempData["Success"] = "Customer updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    // GET: /Customer/Delete/5
    public async Task<IActionResult> Delete(int id)
    {
        var userId = await CurrentUserIdAsync();
        var query = await AccessibleCustomersAsync(userId);

        var customer = await query
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == id);

        if (customer == null)
            return NotFound();

        return View(customer);
    }

    // POST: /Customer/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var userId = await CurrentUserIdAsync();
        var query = await AccessibleCustomersAsync(userId);

        var customer = await query
            .FirstOrDefaultAsync(c => c.CustomerId == id);

        if (customer == null)
            return NotFound();

        var oldValue = JsonSerializer.Serialize(customer);

        var hasOpportunities = await _context.Opportunities
            .AnyAsync(o => o.CustomerId == id);

        var hasFollowUps = await _context.FollowUps
            .AnyAsync(f => f.CustomerId == id);

        var hasActivities = await _context.Activities
            .AnyAsync(a => a.CustomerId == id);

        if (hasOpportunities || hasFollowUps || hasActivities)
        {
            TempData["Error"] =
                "Customer cannot be deleted because related CRM records exist.";

            return RedirectToAction(nameof(Index));
        }

        AddAudit("Delete", customer.CustomerId, oldValue, null);
        _context.Customers.Remove(customer);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Customer deleted successfully.";
        return RedirectToAction(nameof(Index));
    }
}
