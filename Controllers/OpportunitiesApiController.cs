
using System.Security.Claims;
using System.Text.Json;
using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Models.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[ApiController]
[Route("api/opportunities")]
[Authorize(Roles = "Admin,Manager,SalesExecutive")]
public class OpportunitiesApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public OpportunitiesApiController(ApplicationDbContext context)
    {
        _context = context;
    }

    private string CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private bool IsAdmin => User.IsInRole("Admin");

    private IQueryable<Opportunity> AccessibleOpportunities()
    {
        var query = _context.Opportunities
            .Include(o => o.Customer)
            .Include(o => o.Lead)
            .AsQueryable();

        if (!IsAdmin)
        {
            query = query.Where(o => o.AssignedTo == CurrentUserId);
        }

        return query;
    }

    // GET: /api/opportunities
    // GET: /api/opportunities?search=Website&stage=Qualification
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OpportunityApiResponse>>> GetOpportunities(
        [FromQuery] string? search,
        [FromQuery] string? stage,
        [FromQuery] string? status)
    {
        var query = AccessibleOpportunities();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();

            query = query.Where(o =>
                o.OpportunityName.Contains(search) ||
                (o.Customer != null &&
                 o.Customer.CustomerName.Contains(search)) ||
                (o.Lead != null &&
                 o.Lead.LeadName.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(stage))
        {
            query = query.Where(o => o.Stage == stage.Trim());
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(o => o.Status == status.Trim());
        }

        var opportunities = await query
            .OrderByDescending(o => o.CreatedDate)
            .Select(o => new OpportunityApiResponse
            {
                OpportunityId = o.OpportunityId,
                OpportunityName = o.OpportunityName,
                CustomerId = o.CustomerId,
                CustomerName = o.Customer != null
                    ? o.Customer.CustomerName : null,
                LeadId = o.LeadId,
                LeadName = o.Lead != null
                    ? o.Lead.LeadName : null,
                Amount = o.Amount,
                Stage = o.Stage,
                Probability = o.Probability,
                WeightedPipeline = o.Amount * o.Probability / 100m,
                ExpectedCloseDate = o.ExpectedCloseDate,
                Status = o.Status,
                AssignedTo = o.AssignedTo,
                CreatedDate = o.CreatedDate,
                Notes = o.Notes
            })
            .ToListAsync();

        return Ok(opportunities);
    }

    // GET: /api/opportunities/5
    [HttpGet("{id:int}")]
    public async Task<ActionResult<OpportunityApiResponse>> GetOpportunity(int id)
    {
        var opportunity = await AccessibleOpportunities()
            .Where(o => o.OpportunityId == id)
            .Select(o => new OpportunityApiResponse
            {
                OpportunityId = o.OpportunityId,
                OpportunityName = o.OpportunityName,
                CustomerId = o.CustomerId,
                CustomerName = o.Customer != null
                    ? o.Customer.CustomerName : null,
                LeadId = o.LeadId,
                LeadName = o.Lead != null
                    ? o.Lead.LeadName : null,
                Amount = o.Amount,
                Stage = o.Stage,
                Probability = o.Probability,
                WeightedPipeline = o.Amount * o.Probability / 100m,
                ExpectedCloseDate = o.ExpectedCloseDate,
                Status = o.Status,
                AssignedTo = o.AssignedTo,
                CreatedDate = o.CreatedDate,
                Notes = o.Notes
            })
            .FirstOrDefaultAsync();

        if (opportunity == null)
        {
            return NotFound(new { message = "Opportunity not found." });
        }

        return Ok(opportunity);
    }

    // POST: /api/opportunities
    [HttpPost]
    public async Task<ActionResult<OpportunityApiResponse>> CreateOpportunity(
        OpportunityCreateRequest request)
    {
        // Exactly one of CustomerId and LeadId must be supplied.
        if ((request.CustomerId.HasValue && request.LeadId.HasValue) ||
            (!request.CustomerId.HasValue && !request.LeadId.HasValue))
        {
            return BadRequest(new
            {
                message = "Select exactly one Customer or Lead."
            });
        }

        if (request.Status == "Open" &&
            request.ExpectedCloseDate.Date < DateTime.UtcNow.Date)
        {
            return BadRequest(new
            {
                message = "Expected Close Date cannot be in the past."
            });
        }

        Customer? customer = null;
        Lead? lead = null;

        if (request.CustomerId.HasValue)
        {
            var customerQuery = _context.Customers
                .AsQueryable();

            if (!IsAdmin)
            {
                customerQuery = customerQuery.Where(
                    c => c.CreatedBy == CurrentUserId);
            }

            customer = await customerQuery.FirstOrDefaultAsync(
                c => c.CustomerId == request.CustomerId.Value);

            if (customer == null)
            {
                return BadRequest(new
                {
                    message = "The selected customer does not exist or is not accessible."
                });
            }
        }

        if (request.LeadId.HasValue)
        {
            var leadQuery = _context.Leads.AsQueryable();

            if (!IsAdmin)
            {
                leadQuery = leadQuery.Where(
                    l => l.AssignedTo == CurrentUserId);
            }

            lead = await leadQuery.FirstOrDefaultAsync(
                l => l.LeadId == request.LeadId.Value);

            if (lead == null)
            {
                return BadRequest(new
                {
                    message = "The selected lead does not exist or is not accessible."
                });
            }
        }

        var opportunity = new Opportunity
        {
            OpportunityName = request.OpportunityName.Trim(),
            CustomerId = customer?.CustomerId,
            LeadId = lead?.LeadId,
            Amount = request.Amount,
            Stage = request.Stage,
            Probability = request.Probability,
            ExpectedCloseDate = request.ExpectedCloseDate,
            Status = request.Status,
            AssignedTo = CurrentUserId,
            CreatedDate = DateTime.UtcNow,
            Notes = request.Notes?.Trim()
        };

        var response = ToResponse(opportunity, customer, lead);

        _context.Opportunities.Add(opportunity);

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = CurrentUserId,
            Action = "Create",
            EntityName = "Opportunity",
            OldValue = null,
            NewValue = JsonSerializer.Serialize(response),
            CreatedDate = DateTime.UtcNow,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Result = "Success",
            Details = "Created opportunity through REST API."
        });

        await _context.SaveChangesAsync();

        response.OpportunityId = opportunity.OpportunityId;

        return CreatedAtAction(
            nameof(GetOpportunity),
            new { id = opportunity.OpportunityId },
            response);
    }

    private static OpportunityApiResponse ToResponse(
        Opportunity opportunity,
        Customer? customer,
        Lead? lead)
    {
        return new OpportunityApiResponse
        {
            OpportunityId = opportunity.OpportunityId,
            OpportunityName = opportunity.OpportunityName,
            CustomerId = opportunity.CustomerId,
            CustomerName = customer?.CustomerName,
            LeadId = opportunity.LeadId,
            LeadName = lead?.LeadName,
            Amount = opportunity.Amount,
            Stage = opportunity.Stage,
            Probability = opportunity.Probability,
            WeightedPipeline = opportunity.Amount *
                               opportunity.Probability / 100m,
            ExpectedCloseDate = opportunity.ExpectedCloseDate,
            Status = opportunity.Status,
            AssignedTo = opportunity.AssignedTo,
            CreatedDate = opportunity.CreatedDate,
            Notes = opportunity.Notes
        };
    }
}
