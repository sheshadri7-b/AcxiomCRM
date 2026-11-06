
using System.Security.Claims;
using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Models.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[ApiController]
[Route("api/followups")]
[Authorize(Roles = "Admin,Manager,SalesExecutive")]
public class FollowUpsApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public FollowUpsApiController(ApplicationDbContext context)
    {
        _context = context;
    }

    private string CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private bool IsAdmin => User.IsInRole("Admin");

    private IQueryable<FollowUp> AccessibleFollowUps()
    {
        var query = _context.FollowUps
            .Include(f => f.Customer)
            .Include(f => f.Lead)
            .AsQueryable();

        if (!IsAdmin)
        {
            query = query.Where(f => f.AssignedTo == CurrentUserId);
        }

        return query;
    }

    private static FollowUpApiResponse ToResponse(FollowUp followUp)
    {
        return new FollowUpApiResponse
        {
            FollowUpId = followUp.FollowUpId,
            CustomerId = followUp.CustomerId,
            CustomerName = followUp.Customer?.CustomerName,
            LeadId = followUp.LeadId,
            LeadName = followUp.Lead?.LeadName,
            FollowUpDate = followUp.FollowUpDate,
            FollowUpType = followUp.FollowUpType,
            Subject = followUp.Subject,
            Status = followUp.Status,
            AssignedTo = followUp.AssignedTo,
            Remarks = followUp.Remarks,
            CreatedDate = followUp.CreatedDate
        };
    }

    // GET: api/followups
    [HttpGet]
    public async Task<ActionResult<IEnumerable<FollowUpApiResponse>>> GetFollowUps(
        [FromQuery] string? status = null,
        [FromQuery] string? search = null)
    {
        var query = AccessibleFollowUps();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(f => f.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            query = query.Where(f =>
                f.Subject.Contains(term) ||
                (f.Remarks != null && f.Remarks.Contains(term)) ||
                (f.Customer != null &&
                    f.Customer.CustomerName.Contains(term)) ||
                (f.Lead != null &&
                    f.Lead.LeadName.Contains(term)));
        }

        var followUps = await query
            .OrderBy(f => f.FollowUpDate)
            .Select(f => new FollowUpApiResponse
            {
                FollowUpId = f.FollowUpId,
                CustomerId = f.CustomerId,
                CustomerName = f.Customer != null
                    ? f.Customer.CustomerName : null,
                LeadId = f.LeadId,
                LeadName = f.Lead != null
                    ? f.Lead.LeadName : null,
                FollowUpDate = f.FollowUpDate,
                FollowUpType = f.FollowUpType,
                Subject = f.Subject,
                Status = f.Status,
                AssignedTo = f.AssignedTo,
                Remarks = f.Remarks,
                CreatedDate = f.CreatedDate
            })
            .ToListAsync();

        return Ok(followUps);
    }

    // POST: api/followups
    [HttpPost]
    public async Task<ActionResult<FollowUpApiResponse>> CreateFollowUp(
        [FromBody] FollowUpCreateRequest request)
    {
        // A follow-up must belong to exactly one Customer or Lead.
        if ((request.CustomerId.HasValue && request.LeadId.HasValue) ||
            (!request.CustomerId.HasValue && !request.LeadId.HasValue))
        {
            return BadRequest(new
            {
                message = "Provide exactly one CustomerId or LeadId."
            });
        }

        // New follow-ups are planned and cannot be scheduled in the past.
        if (request.FollowUpDate.Date < DateTime.Today)
        {
            return BadRequest(new
            {
                message = "A planned follow-up cannot be scheduled in the past."
            });
        }

        if (request.CustomerId.HasValue)
        {
            var customerQuery = _context.Customers
                .Where(c => c.CustomerId == request.CustomerId.Value);

            if (!IsAdmin)
            {
                customerQuery = customerQuery.Where(
                    c => c.CreatedBy == CurrentUserId);
            }

            if (!await customerQuery.AnyAsync())
            {
                return NotFound(new
                {
                    message = "Customer not found or access denied."
                });
            }
        }

        if (request.LeadId.HasValue)
        {
            var leadQuery = _context.Leads
                .Where(l => l.LeadId == request.LeadId.Value);

            if (!IsAdmin)
            {
                leadQuery = leadQuery.Where(
                    l => l.AssignedTo == CurrentUserId);
            }

            if (!await leadQuery.AnyAsync())
            {
                return NotFound(new
                {
                    message = "Lead not found or access denied."
                });
            }
        }

        var followUp = new FollowUp
        {
            CustomerId = request.CustomerId,
            LeadId = request.LeadId,
            FollowUpDate = request.FollowUpDate,
            FollowUpType = request.FollowUpType,
            Subject = request.Subject.Trim(),
            Status = "Planned",
            AssignedTo = CurrentUserId,
            Remarks = request.Remarks,
            CreatedDate = DateTime.UtcNow
        };

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            _context.FollowUps.Add(followUp);
            await _context.SaveChangesAsync();

            _context.AuditLogs.Add(new AuditLog
            {
                UserId = CurrentUserId,
                Action = "Create",
                EntityName = "FollowUp",
                RecordId = followUp.FollowUpId,
                NewValue = System.Text.Json.JsonSerializer.Serialize(
                    ToResponse(followUp)),
                CreatedDate = DateTime.UtcNow,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                Result = "Success",
                Details = "Follow-up created through API."
            });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            var response = await AccessibleFollowUps()
                .FirstAsync(f => f.FollowUpId == followUp.FollowUpId);

            return CreatedAtAction(
                nameof(GetFollowUps),
                new { },
                ToResponse(response));
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
