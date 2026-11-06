
using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public HomeController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    [Authorize]
    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User);

        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        var isAdmin = User.IsInRole("Admin");
        var isManager = User.IsInRole("Manager");

        // Determine which users' records this user can access.
        var accessibleUserIds = new List<string> { userId };

        if (isManager)
        {
            var teamMemberIds = await _userManager.Users
                .Where(u => u.ManagerId == userId)
                .Select(u => u.Id)
                .ToListAsync();

            accessibleUserIds.AddRange(teamMemberIds);
        }

        // Base queries.
        var customersQuery = _context.Customers.AsNoTracking();
        var leadsQuery = _context.Leads.AsNoTracking();
        var opportunitiesQuery =
            _context.Opportunities.AsNoTracking();

        // Apply role-based data scope.
        if (!isAdmin)
        {
            customersQuery = customersQuery.Where(
                c => c.CreatedBy != null &&
                     accessibleUserIds.Contains(c.CreatedBy));

            leadsQuery = leadsQuery.Where(
                l => l.AssignedTo != null &&
                     accessibleUserIds.Contains(l.AssignedTo));

            opportunitiesQuery = opportunitiesQuery.Where(
                o => o.AssignedTo != null &&
                     accessibleUserIds.Contains(o.AssignedTo));
        }

        // Retrieve scoped records.
        var customers = await customersQuery.ToListAsync();
        var leads = await leadsQuery.ToListAsync();
        var opportunities = await opportunitiesQuery.ToListAsync();

        // Calculate dashboard KPIs.
        var model = new DashboardViewModel
        {
            TotalCustomers = customers.Count,

            TotalLeads = leads.Count,

            OpenLeads = leads.Count(l =>
                l.Status == "New" ||
                l.Status == "Contacted" ||
                l.Status == "Qualified"),

            TotalOpportunities = opportunities.Count,

            OpenOpportunities = opportunities.Count(o =>
                o.Status == "Open" &&
                o.Stage != "Won" &&
                o.Stage != "Lost"),

            WonOpportunities = opportunities.Count(o =>
                o.Stage == "Won"),

            LostOpportunities = opportunities.Count(o =>
                o.Stage == "Lost"),

            PipelineValue = opportunities
                .Where(o =>
                    o.Status == "Open" &&
                    o.Stage != "Won" &&
                    o.Stage != "Lost")
                .Sum(o => o.Amount * o.Probability / 100m)
        };

        // Lead status chart.
        var leadStatuses = new[]
        {
            "New",
            "Contacted",
            "Qualified",
            "Unqualified",
            "Converted",
            "Lost"
        };

        foreach (var status in leadStatuses)
        {
            model.LeadStatusCounts[status] =
                leads.Count(l => l.Status == status);
        }

        // Opportunity pipeline chart.
        var stages = new[]
        {
            "Qualification",
            "Proposal",
            "Negotiation",
            "Won",
            "Lost"
        };

        foreach (var stage in stages)
        {
            model.OpportunityStageCounts[stage] =
                opportunities.Count(o => o.Stage == stage);
        }

        // Monthly won-opportunity totals.
        // CreatedDate is temporary because Opportunity does
        // not yet contain a WonDate property.
        var startDate = new DateTime(
            DateTime.UtcNow.Year,
            DateTime.UtcNow.Month,
            1).AddMonths(-5);

        var wonOpportunities = opportunities
            .Where(o =>
                o.Stage == "Won" &&
                o.CreatedDate >= startDate)
            .ToList();

        for (int i = 5; i >= 0; i--)
        {
            var month = DateTime.UtcNow.AddMonths(-i);
            var key = month.ToString("MMM yyyy");

            model.MonthlySales[key] = wonOpportunities
                .Where(o =>
                    o.CreatedDate.Year == month.Year &&
                    o.CreatedDate.Month == month.Month)
                .Sum(o => o.Amount);
        }

        return View(model);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(
        Duration = 0,
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel
        {
            RequestId =
                System.Diagnostics.Activity.Current?.Id
                ?? HttpContext.TraceIdentifier
        });
    }
}
