
using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace AcxiomCRM.Controllers;

[Authorize(Roles = "Admin,Manager,SalesExecutive")]
public class ReportsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    private string CurrentUserId => _userManager.GetUserId(User)!;
    private bool IsAdmin => User.IsInRole("Admin");

    private const int DefaultPageSize = 10;

    private static readonly string[] ValidReportTypes =
    {
        "Customers",
        "Leads",
        "FollowUps",
        "Opportunities",
        "Pipeline",
        "Sales",
        "UserActivity",
        "Audit"
    };

    private static readonly Dictionary<string, string[]> ValidStatuses = new()
    {
        ["Customers"] = new[] { "Active", "Inactive" },

        ["Leads"] = new[]
        {
            "New", "Contacted", "Qualified",
            "Unqualified", "Converted", "Lost"
        },

        ["FollowUps"] = new[]
        {
            "Planned", "Completed", "Missed", "Cancelled"
        },

        ["Opportunities"] = new[] { "Open", "Won", "Lost" },

        ["Pipeline"] = new[] { "Open" },

        ["Sales"] = new[] { "Won" },

        ["UserActivity"] = new[] { "Success", "Failure" },

        ["Audit"] = new[] { "Success", "Failure" }
    };

    public ReportsController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // ---------------------------------------------------------
    // ACCESS CONTROL
    // ---------------------------------------------------------

    private IQueryable<Customer> AccessibleCustomers()
    {
        var query = _context.Customers.AsQueryable();

        if (!IsAdmin)
        {
            query = query.Where(c => c.CreatedBy == CurrentUserId);
        }

        return query;
    }

    private IQueryable<Lead> AccessibleLeads()
    {
        var query = _context.Leads.AsQueryable();

        if (!IsAdmin)
        {
            query = query.Where(l => l.AssignedTo == CurrentUserId);
        }

        return query;
    }

    private IQueryable<Opportunity> AccessibleOpportunities()
    {
        var query = _context.Opportunities.AsQueryable();

        if (!IsAdmin)
        {
            query = query.Where(o => o.AssignedTo == CurrentUserId);
        }

        return query;
    }

    private IQueryable<FollowUp> AccessibleFollowUps()
    {
        var query = _context.FollowUps.AsQueryable();

        if (!IsAdmin)
        {
            query = query.Where(f => f.AssignedTo == CurrentUserId);
        }

        return query;
    }

    private IQueryable<Activity> AccessibleActivities()
    {
        var query = _context.Activities.AsQueryable();

        if (!IsAdmin)
        {
            query = query.Where(a => a.AssignedTo == CurrentUserId);
        }

        return query;
    }

    private IQueryable<AuditLog> AccessibleAuditLogs()
    {
        // Audit and User Activity reports are Admin-only.
        return _context.AuditLogs.AsQueryable();
    }

    // ---------------------------------------------------------
    // FILTER VALIDATION
    // ---------------------------------------------------------

    private static string NormalizeReportType(string? reportType)
    {
        return !string.IsNullOrWhiteSpace(reportType) &&
               ValidReportTypes.Contains(reportType)
            ? reportType
            : "Customers";
    }

    private static string? NormalizeStatus(
        string reportType,
        string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        if (!ValidStatuses.TryGetValue(reportType, out var allowed))
        {
            return null;
        }

        return allowed.Contains(status) ? status : null;
    }

    private static string? NormalizeStage(
        string reportType,
        string? stage)
    {
        if (reportType != "Opportunities" &&
            reportType != "Pipeline" &&
            reportType != "Sales")
        {
            return null;
        }

        string[] allowedStages =
        {
            "Qualification",
            "Proposal",
            "Negotiation",
            "Won",
            "Lost"
        };

        return allowedStages.Contains(stage ?? "")
            ? stage
            : null;
    }

    private static void NormalizeDates(
        ref DateTime? fromDate,
        ref DateTime? toDate)
    {
        if (fromDate.HasValue)
        {
            fromDate = fromDate.Value.Date;
        }

        if (toDate.HasValue)
        {
            toDate = toDate.Value.Date;
        }
    }

    private bool CanAccessReport(string reportType)
    {
        if (!IsAdmin &&
            (reportType == "UserActivity" || reportType == "Audit"))
        {
            return false;
        }

        return true;
    }

    // ---------------------------------------------------------
    // REPORT INDEX
    // ---------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Index(
        string reportType = "Customers",
        string? search = null,
        string? status = null,
        string? stage = null,
        string? assignedTo = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int page = 1)
    {
        reportType = NormalizeReportType(reportType);

        if (!CanAccessReport(reportType))
        {
            return Forbid();
        }

        status = NormalizeStatus(reportType, status);
        stage = NormalizeStage(reportType, stage);

        NormalizeDates(ref fromDate, ref toDate);

        if (fromDate.HasValue &&
            toDate.HasValue &&
            fromDate.Value > toDate.Value)
        {
            ModelState.AddModelError(
                "",
                "From Date cannot be later than To Date.");

            fromDate = null;
            toDate = null;
        }

        page = Math.Max(page, 1);

        var model = new ReportsViewModel
        {
            ReportType = reportType,
            Search = search?.Trim(),
            Status = status,
            Stage = stage,
            AssignedTo = IsAdmin ? assignedTo?.Trim() : null,
            FromDate = fromDate,
            ToDate = toDate,
            CurrentPage = page,
            PageSize = DefaultPageSize
        };

        await PopulateSummaryAsync(model);

        switch (reportType)
        {
            case "Customers":
                await LoadCustomersAsync(model);
                break;

            case "Leads":
                await LoadLeadsAsync(model);
                break;

            case "FollowUps":
                await LoadFollowUpsAsync(model);
                break;

            case "Opportunities":
            case "Pipeline":
            case "Sales":
                await LoadOpportunitiesAsync(model);
                break;

            case "UserActivity":
                await LoadUserActivityAsync(model);
                break;

            case "Audit":
                await LoadAuditAsync(model);
                break;
        }

        return View(model);
    }

    // ---------------------------------------------------------
    // SUMMARY CARDS
    // ---------------------------------------------------------

    private async Task PopulateSummaryAsync(ReportsViewModel model)
    {
        var customers = AccessibleCustomers();
        var leads = AccessibleLeads();
        var opportunities = AccessibleOpportunities();
        var followUps = AccessibleFollowUps();
        var activities = AccessibleActivities();

        model.TotalCustomers = await customers.CountAsync();
        model.TotalLeads = await leads.CountAsync();
        model.TotalOpportunities = await opportunities.CountAsync();
        model.TotalFollowUps = await followUps.CountAsync();
        model.TotalActivities = await activities.CountAsync();

        model.TotalPipelineValue = await opportunities
            .Where(o => o.Status == "Open")
            .SumAsync(o => (decimal?)o.Amount) ?? 0m;

        model.WeightedPipelineValue = await opportunities
            .Where(o => o.Status == "Open")
            .SumAsync(o =>
                (decimal?)(o.Amount * o.Probability / 100m)) ?? 0m;

        model.TotalWonValue = await opportunities
            .Where(o => o.Status == "Won")
            .SumAsync(o => (decimal?)o.Amount) ?? 0m;

        model.ConvertedLeads = await leads
            .CountAsync(l => l.Status == "Converted");

        model.WonOpportunities = await opportunities
            .CountAsync(o => o.Status == "Won");

        model.LostOpportunities = await opportunities
            .CountAsync(o => o.Status == "Lost");
    }

    // ---------------------------------------------------------
    // CUSTOMER REPORT
    // ---------------------------------------------------------

    private async Task LoadCustomersAsync(ReportsViewModel model)
    {
        var query = AccessibleCustomers();

        if (!string.IsNullOrWhiteSpace(model.Search))
        {
            var search = model.Search;

            query = query.Where(c =>
                c.CustomerName.Contains(search) ||
                c.CustomerCode.Contains(search) ||
                c.Email.Contains(search) ||
                c.Phone.Contains(search) ||
                (c.CompanyName != null &&
                 c.CompanyName.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(model.Status))
        {
            query = query.Where(c => c.Status == model.Status);
        }

        if (IsAdmin &&
            !string.IsNullOrWhiteSpace(model.AssignedTo))
        {
            query = query.Where(c => c.CreatedBy == model.AssignedTo);
        }

        if (model.FromDate.HasValue)
        {
            query = query.Where(c =>
                c.CreatedDate >= model.FromDate.Value);
        }

        if (model.ToDate.HasValue)
        {
            var endDate = model.ToDate.Value.Date.AddDays(1);

            query = query.Where(c => c.CreatedDate < endDate);
        }

        model.TotalRecords = await query.CountAsync();

        model.Customers = await query
            .OrderByDescending(c => c.CreatedDate)
            .ThenByDescending(c => c.CustomerId)
            .Skip((model.CurrentPage - 1) * model.PageSize)
            .Take(model.PageSize)
            .Select(c => new CustomerReportRow
            {
                CustomerId = c.CustomerId,
                CustomerCode = c.CustomerCode,
                CustomerName = c.CustomerName,
                CompanyName = c.CompanyName,
                Email = c.Email,
                Phone = c.Phone,
                Status = c.Status,
                CreatedDate = c.CreatedDate
            })
            .ToListAsync();
    }

    // ---------------------------------------------------------
    // LEAD REPORT
    // ---------------------------------------------------------

    private async Task LoadLeadsAsync(ReportsViewModel model)
    {
        var query = AccessibleLeads();

        if (!string.IsNullOrWhiteSpace(model.Search))
        {
            var search = model.Search;

            query = query.Where(l =>
                l.LeadName.Contains(search) ||
                l.LeadCode.Contains(search) ||
                (l.Email != null && l.Email.Contains(search)) ||
                (l.CompanyName != null &&
                 l.CompanyName.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(model.Status))
        {
            query = query.Where(l => l.Status == model.Status);
        }

        if (IsAdmin &&
            !string.IsNullOrWhiteSpace(model.AssignedTo))
        {
            query = query.Where(l =>
                l.AssignedTo == model.AssignedTo);
        }

        if (model.FromDate.HasValue)
        {
            query = query.Where(l =>
                l.CreatedDate >= model.FromDate.Value);
        }

        if (model.ToDate.HasValue)
        {
            var endDate = model.ToDate.Value.Date.AddDays(1);

            query = query.Where(l => l.CreatedDate < endDate);
        }

        model.TotalRecords = await query.CountAsync();

        model.Leads = await query
            .OrderByDescending(l => l.CreatedDate)
            .ThenByDescending(l => l.LeadId)
            .Skip((model.CurrentPage - 1) * model.PageSize)
            .Take(model.PageSize)
            .Select(l => new LeadReportRow
            {
                LeadId = l.LeadId,
                LeadCode = l.LeadCode,
                LeadName = l.LeadName,
                CompanyName = l.CompanyName,
                Status = l.Status,
                Priority = l.Priority,
                Source = l.Source,
                ExpectedValue = l.ExpectedValue,
                CreatedDate = l.CreatedDate
            })
            .ToListAsync();
    }

    // ---------------------------------------------------------
    // FOLLOW-UP REPORT
    // ---------------------------------------------------------

    private async Task LoadFollowUpsAsync(ReportsViewModel model)
    {
        var query = AccessibleFollowUps();

        if (!string.IsNullOrWhiteSpace(model.Search))
        {
            var search = model.Search;

            query = query.Where(f =>
                f.Subject.Contains(search) ||
                (f.Remarks != null &&
                 f.Remarks.Contains(search)) ||
                (f.Customer != null &&
                 f.Customer.CustomerName.Contains(search)) ||
                (f.Lead != null &&
                 f.Lead.LeadName.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(model.Status))
        {
            query = query.Where(f => f.Status == model.Status);
        }

        if (IsAdmin &&
            !string.IsNullOrWhiteSpace(model.AssignedTo))
        {
            query = query.Where(f =>
                f.AssignedTo == model.AssignedTo);
        }

        if (model.FromDate.HasValue)
        {
            query = query.Where(f =>
                f.FollowUpDate >= model.FromDate.Value);
        }

        if (model.ToDate.HasValue)
        {
            var endDate = model.ToDate.Value.Date.AddDays(1);

            query = query.Where(f =>
                f.FollowUpDate < endDate);
        }

        model.TotalRecords = await query.CountAsync();

        model.FollowUps = await query
            .OrderByDescending(f => f.FollowUpDate)
            .ThenByDescending(f => f.FollowUpId)
            .Skip((model.CurrentPage - 1) * model.PageSize)
            .Take(model.PageSize)
            .Select(f => new FollowUpReportRow
            {
                FollowUpId = f.FollowUpId,
                Subject = f.Subject,
                FollowUpType = f.FollowUpType,
                CustomerName = f.Customer != null
                    ? f.Customer.CustomerName
                    : null,
                LeadName = f.Lead != null
                    ? f.Lead.LeadName
                    : null,
                FollowUpDate = f.FollowUpDate,
                Status = f.Status
            })
            .ToListAsync();
    }

    // ---------------------------------------------------------
    // OPPORTUNITY / PIPELINE / SALES REPORT
    // ---------------------------------------------------------

    private async Task LoadOpportunitiesAsync(ReportsViewModel model)
    {
        var query = AccessibleOpportunities();

        if (!string.IsNullOrWhiteSpace(model.Search))
        {
            var search = model.Search;

            query = query.Where(o =>
                o.OpportunityName.Contains(search) ||
                (o.Customer != null &&
                 o.Customer.CustomerName.Contains(search)) ||
                (o.Lead != null &&
                 o.Lead.LeadName.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(model.Status))
        {
            query = query.Where(o => o.Status == model.Status);
        }

        if (!string.IsNullOrWhiteSpace(model.Stage))
        {
            query = query.Where(o => o.Stage == model.Stage);
        }

        if (IsAdmin &&
            !string.IsNullOrWhiteSpace(model.AssignedTo))
        {
            query = query.Where(o =>
                o.AssignedTo == model.AssignedTo);
        }

        if (model.FromDate.HasValue)
        {
            query = query.Where(o =>
                o.CreatedDate >= model.FromDate.Value);
        }

        if (model.ToDate.HasValue)
        {
            var endDate = model.ToDate.Value.Date.AddDays(1);

            query = query.Where(o =>
                o.CreatedDate < endDate);
        }

        // Pipeline contains open opportunities only.
        if (model.ReportType == "Pipeline")
        {
            query = query.Where(o => o.Status == "Open");
        }

        // Sales contains won opportunities only.
        if (model.ReportType == "Sales")
        {
            query = query.Where(o => o.Status == "Won");
        }

        model.TotalRecords = await query.CountAsync();

        model.Opportunities = await query
            .OrderByDescending(o => o.CreatedDate)
            .ThenByDescending(o => o.OpportunityId)
            .Skip((model.CurrentPage - 1) * model.PageSize)
            .Take(model.PageSize)
            .Select(o => new OpportunityReportRow
            {
                OpportunityId = o.OpportunityId,
                OpportunityName = o.OpportunityName,
                CustomerName = o.Customer != null
                    ? o.Customer.CustomerName
                    : null,
                LeadName = o.Lead != null
                    ? o.Lead.LeadName
                    : null,
                Amount = o.Amount,
                Probability = o.Probability,
                WeightedPipeline =
                    o.Amount * o.Probability / 100m,
                Stage = o.Stage,
                Status = o.Status,
                ExpectedCloseDate = o.ExpectedCloseDate
            })
            .ToListAsync();
    }

    // ---------------------------------------------------------
    // USER ACTIVITY REPORT
    // ---------------------------------------------------------

    private async Task LoadUserActivityAsync(ReportsViewModel model)
    {
        var query = AccessibleAuditLogs();

        if (!string.IsNullOrWhiteSpace(model.Search))
        {
            var search = model.Search;

            query = query.Where(a =>
                (a.UserId != null && a.UserId.Contains(search)) ||
                a.Action.Contains(search) ||
                a.EntityName.Contains(search) ||
                (a.Details != null &&
                 a.Details.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(model.Status))
        {
            query = query.Where(a => a.Result == model.Status);
        }

        if (IsAdmin &&
            !string.IsNullOrWhiteSpace(model.AssignedTo))
        {
            query = query.Where(a =>
                a.UserId == model.AssignedTo);
        }

        if (model.FromDate.HasValue)
        {
            query = query.Where(a =>
                a.CreatedDate >= model.FromDate.Value);
        }

        if (model.ToDate.HasValue)
        {
            var endDate = model.ToDate.Value.Date.AddDays(1);

            query = query.Where(a =>
                a.CreatedDate < endDate);
        }

        var activityGroups = await query
            .Where(a => a.UserId != null)
            .GroupBy(a => a.UserId!)
            .Select(g => new
            {
                UserId = g.Key,
                ActivityCount = g.Count()
            })
            .OrderByDescending(g => g.ActivityCount)
            .ToListAsync();

        var users = await _context.Users
            .Select(u => new
            {
                u.Id,
                u.FullName
            })
            .ToDictionaryAsync(
                u => u.Id,
                u => u.FullName);

        model.TotalRecords = activityGroups.Count;

        model.UserActivities = activityGroups
            .Skip((model.CurrentPage - 1) * model.PageSize)
            .Take(model.PageSize)
            .Select(g => new UserActivityReportRow
            {
                UserId = g.UserId,
                UserName = users.GetValueOrDefault(g.UserId)
                    ?? g.UserId,
                ActivityCount = g.ActivityCount
            })
            .ToList();
    }

    // ---------------------------------------------------------
    // AUDIT REPORT
    // ---------------------------------------------------------

    private async Task LoadAuditAsync(ReportsViewModel model)
    {
        var query = AccessibleAuditLogs();

        if (!string.IsNullOrWhiteSpace(model.Search))
        {
            var search = model.Search;

            query = query.Where(a =>
                (a.UserId != null && a.UserId.Contains(search)) ||
                a.Action.Contains(search) ||
                a.EntityName.Contains(search) ||
                (a.Details != null &&
                 a.Details.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(model.Status))
        {
            query = query.Where(a => a.Result == model.Status);
        }

        if (IsAdmin &&
            !string.IsNullOrWhiteSpace(model.AssignedTo))
        {
            query = query.Where(a =>
                a.UserId == model.AssignedTo);
        }

        if (model.FromDate.HasValue)
        {
            query = query.Where(a =>
                a.CreatedDate >= model.FromDate.Value);
        }

        if (model.ToDate.HasValue)
        {
            var endDate = model.ToDate.Value.Date.AddDays(1);

            query = query.Where(a =>
                a.CreatedDate < endDate);
        }

        model.TotalRecords = await query.CountAsync();

        model.AuditLogs = await query
            .OrderByDescending(a => a.CreatedDate)
            .ThenByDescending(a => a.AuditLogId)
            .Skip((model.CurrentPage - 1) * model.PageSize)
            .Take(model.PageSize)
            .Select(a => new AuditReportRow
            {
                AuditLogId = a.AuditLogId,
                UserId = a.UserId,
                Action = a.Action,
                EntityName = a.EntityName,
                RecordId = a.RecordId,
                Result = a.Result,
                CreatedDate = a.CreatedDate
            })
            .ToListAsync();
    }

    // ---------------------------------------------------------
    // CSV EXPORT
    // ---------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Export(
        string reportType = "Customers",
        string? search = null,
        string? status = null,
        string? stage = null,
        string? assignedTo = null,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        reportType = NormalizeReportType(reportType);

        if (!CanAccessReport(reportType))
        {
            return Forbid();
        }

        status = NormalizeStatus(reportType, status);
        stage = NormalizeStage(reportType, stage);

        NormalizeDates(ref fromDate, ref toDate);

        if (fromDate.HasValue &&
            toDate.HasValue &&
            fromDate.Value > toDate.Value)
        {
            return BadRequest(
                "From Date cannot be later than To Date.");
        }

        var model = new ReportsViewModel
        {
            ReportType = reportType,
            Search = search?.Trim(),
            Status = status,
            Stage = stage,
            AssignedTo = IsAdmin ? assignedTo?.Trim() : null,
            FromDate = fromDate,
            ToDate = toDate,
            CurrentPage = 1,
            PageSize = int.MaxValue
        };

        switch (reportType)
        {
            case "Customers":
                await LoadCustomersAsync(model);
                break;

            case "Leads":
                await LoadLeadsAsync(model);
                break;

            case "FollowUps":
                await LoadFollowUpsAsync(model);
                break;

            case "Opportunities":
            case "Pipeline":
            case "Sales":
                await LoadOpportunitiesAsync(model);
                break;

            case "UserActivity":
                await LoadUserActivityAsync(model);
                break;

            case "Audit":
                await LoadAuditAsync(model);
                break;
        }

        var csv = new StringBuilder();

        switch (reportType)
        {
            case "Customers":
                csv.AppendLine(
                    "CustomerCode,CustomerName,Company,Email,Phone,Status,CreatedDate");

                foreach (var c in model.Customers)
                {
                    csv.AppendLine(Csv(
                        c.CustomerCode,
                        c.CustomerName,
                        c.CompanyName,
                        c.Email,
                        c.Phone,
                        c.Status,
                        c.CreatedDate.ToString("yyyy-MM-dd")));
                }
                break;

            case "Leads":
                csv.AppendLine(
                    "LeadCode,LeadName,Company,Status,Priority,Source,ExpectedValue,CreatedDate");

                foreach (var l in model.Leads)
                {
                    csv.AppendLine(Csv(
                        l.LeadCode,
                        l.LeadName,
                        l.CompanyName,
                        l.Status,
                        l.Priority,
                        l.Source,
                        l.ExpectedValue,
                        l.CreatedDate.ToString("yyyy-MM-dd")));
                }
                break;

            case "FollowUps":
                csv.AppendLine(
                    "Subject,Type,Customer,Lead,FollowUpDate,Status");

                foreach (var f in model.FollowUps)
                {
                    csv.AppendLine(Csv(
                        f.Subject,
                        f.FollowUpType,
                        f.CustomerName,
                        f.LeadName,
                        f.FollowUpDate.ToString("yyyy-MM-dd"),
                        f.Status));
                }
                break;

            case "Opportunities":
            case "Pipeline":
            case "Sales":
                csv.AppendLine(
                    "Opportunity,Customer,Lead,Amount,Probability,WeightedValue,Stage,Status,ExpectedCloseDate");

                foreach (var o in model.Opportunities)
                {
                    csv.AppendLine(Csv(
                        o.OpportunityName,
                        o.CustomerName,
                        o.LeadName,
                        o.Amount,
                        o.Probability,
                        o.WeightedPipeline,
                        o.Stage,
                        o.Status,
                        o.ExpectedCloseDate.ToString("yyyy-MM-dd")));
                }
                break;

            case "UserActivity":
                csv.AppendLine("UserId,UserName,ActivityCount");

                foreach (var u in model.UserActivities)
                {
                    csv.AppendLine(Csv(
                        u.UserId,
                        u.UserName,
                        u.ActivityCount));
                }
                break;

            case "Audit":
                csv.AppendLine(
                    "AuditLogId,UserId,Action,Entity,RecordId,Result,CreatedDate");

                foreach (var a in model.AuditLogs)
                {
                    csv.AppendLine(Csv(
                        a.AuditLogId,
                        a.UserId,
                        a.Action,
                        a.EntityName,
                        a.RecordId,
                        a.Result,
                        a.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss")));
                }
                break;
        }

        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(csv.ToString()))
            .ToArray();

        return File(
            bytes,
            "text/csv; charset=utf-8",
            $"{reportType}_Report_{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }

    // ---------------------------------------------------------
    // CSV FORMATTING AND FORMULA INJECTION PROTECTION
    // ---------------------------------------------------------

    private static string Csv(params object?[] values)
    {
        return string.Join(",", values.Select(value =>
        {
            var text = Convert.ToString(
                value,
                System.Globalization.CultureInfo.InvariantCulture) ?? "";

            // Prevent spreadsheet formula injection.
            if (text.Length > 0 &&
                "=+-@".Contains(text[0]))
            {
                text = "'" + text;
            }

            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }));
    }
}
