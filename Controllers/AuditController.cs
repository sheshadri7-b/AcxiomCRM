using AcxiomCRM.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[Authorize(Roles = "Admin")]
public class AuditController : Controller
{
    private readonly ApplicationDbContext _context;

    public AuditController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: /Audit
    public async Task<IActionResult> Index(
        string? search,
        string? actionType,
        string? entityName,
        string? result,
        DateTime? fromDate,
        DateTime? toDate,
        int page = 1)
    {
        const int pageSize = 15;

        var query = _context.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();

            query = query.Where(a =>
                (a.UserId != null && a.UserId.Contains(search)) ||
                a.Action.Contains(search) ||
                a.EntityName.Contains(search) ||
                (a.Details != null && a.Details.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(actionType))
            query = query.Where(a => a.Action == actionType);

        if (!string.IsNullOrWhiteSpace(entityName))
            query = query.Where(a => a.EntityName == entityName);

        if (!string.IsNullOrWhiteSpace(result))
            query = query.Where(a => a.Result == result);

        if (fromDate.HasValue)
        {
            var start = fromDate.Value.Date;
            query = query.Where(a => a.CreatedDate >= start);
        }

        if (toDate.HasValue)
        {
            var endExclusive = toDate.Value.Date.AddDays(1);
            query = query.Where(a => a.CreatedDate < endExclusive);
        }

        var totalRecords = await query.CountAsync();

        var logs = await query
            .OrderByDescending(a => a.CreatedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Search = search;
        ViewBag.ActionType = actionType;
        ViewBag.EntityName = entityName;
        ViewBag.Result = result;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages =
            (int)Math.Ceiling(totalRecords / (double)pageSize);
        ViewBag.TotalRecords = totalRecords;

        ViewBag.Actions = new[]
        {
            "Create", "Update", "Delete", "Login", "Logout"
        };

        ViewBag.Entities = new[]
        {
            "Customer", "Lead", "Opportunity",
            "FollowUp", "Activity", "User", "Authentication"
        };

        ViewBag.Results = new[] { "Success", "Failure" };

        return View(logs);
    }

    // GET: /Audit/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
            return NotFound();

        var auditLog = await _context.AuditLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AuditLogId == id);

        if (auditLog == null)
            return NotFound();

        return View(auditLog);
    }
}