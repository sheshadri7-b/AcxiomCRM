
using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.Models.ViewModels;

public class ReportsViewModel
{
    // Report selection
    public string ReportType { get; set; } = "Customers";

    // Filters
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? Stage { get; set; }
    public string? AssignedTo { get; set; }

    [DataType(DataType.Date)]
    public DateTime? FromDate { get; set; }

    [DataType(DataType.Date)]
    public DateTime? ToDate { get; set; }

    // Pagination
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalRecords { get; set; }

    public int TotalPages =>
        (int)Math.Ceiling(TotalRecords / (double)PageSize);

    // Summary metrics
    public int TotalCustomers { get; set; }
    public int TotalLeads { get; set; }
    public int TotalOpportunities { get; set; }
    public int TotalFollowUps { get; set; }
    public int TotalActivities { get; set; }

    public decimal TotalPipelineValue { get; set; }
    public decimal WeightedPipelineValue { get; set; }
    public decimal TotalWonValue { get; set; }

    public int ConvertedLeads { get; set; }
    public int WonOpportunities { get; set; }
    public int LostOpportunities { get; set; }

    // Report data
    public List<CustomerReportRow> Customers { get; set; } = new();
    public List<LeadReportRow> Leads { get; set; } = new();
    public List<OpportunityReportRow> Opportunities { get; set; } = new();
    public List<FollowUpReportRow> FollowUps { get; set; } = new();
    public List<ActivityReportRow> Activities { get; set; } = new();
    public List<UserActivityReportRow> UserActivities { get; set; } = new();
    public List<AuditReportRow> AuditLogs { get; set; } = new();
}

public class CustomerReportRow
{
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string? CompanyName { get; set; }
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedDate { get; set; }
}

public class LeadReportRow
{
    public int LeadId { get; set; }
    public string LeadCode { get; set; } = "";
    public string LeadName { get; set; } = "";
    public string? CompanyName { get; set; }
    public string Status { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Source { get; set; } = "";
    public decimal ExpectedValue { get; set; }
    public DateTime CreatedDate { get; set; }
}

public class OpportunityReportRow
{
    public int OpportunityId { get; set; }
    public string OpportunityName { get; set; } = "";
    public string? CustomerName { get; set; }
    public string? LeadName { get; set; }
    public decimal Amount { get; set; }
    public int Probability { get; set; }
    public decimal WeightedPipeline { get; set; }
    public string Stage { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime ExpectedCloseDate { get; set; }
}

public class FollowUpReportRow
{
    public int FollowUpId { get; set; }
    public string Subject { get; set; } = "";
    public string FollowUpType { get; set; } = "";
    public string? CustomerName { get; set; }
    public string? LeadName { get; set; }
    public DateTime FollowUpDate { get; set; }
    public string Status { get; set; } = "";
}

public class ActivityReportRow
{
    public int ActivityId { get; set; }
    public string ActivityType { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTime ActivityDate { get; set; }
    public string Status { get; set; } = "";
}

public class UserActivityReportRow
{
    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";
    public int ActivityCount { get; set; }
}

public class AuditReportRow
{
    public int AuditLogId { get; set; }
    public string? UserId { get; set; }
    public string Action { get; set; } = "";
    public string EntityName { get; set; } = "";
    public int? RecordId { get; set; }
    public string Result { get; set; } = "";
    public DateTime CreatedDate { get; set; }
}
