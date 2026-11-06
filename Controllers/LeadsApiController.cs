
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
[Route("api/leads")]
[Authorize(Roles = "Admin,Manager,SalesExecutive")]
public class LeadsApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public LeadsApiController(ApplicationDbContext context)
    {
        _context = context;
    }

    private string CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private bool IsAdmin => User.IsInRole("Admin");

    private IQueryable<Lead> AccessibleLeads()
    {
        var query = _context.Leads.AsQueryable();

        if (!IsAdmin)
        {
            query = query.Where(l => l.AssignedTo == CurrentUserId);
        }

        return query;
    }

    // GET: /api/leads
    // GET: /api/leads?search=John&status=New
    [HttpGet]
    public async Task<ActionResult<IEnumerable<LeadApiResponse>>> GetLeads(
        [FromQuery] string? search,
        [FromQuery] string? status)
    {
        var query = AccessibleLeads();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();

            query = query.Where(l =>
                l.LeadName.Contains(search) ||
                l.LeadCode.Contains(search) ||
                (l.Email != null && l.Email.Contains(search)) ||
                (l.CompanyName != null && l.CompanyName.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(l => l.Status == status.Trim());
        }

        var leads = await query
            .OrderByDescending(l => l.CreatedDate)
            .Select(l => new LeadApiResponse
            {
                LeadId = l.LeadId,
                LeadCode = l.LeadCode,
                LeadName = l.LeadName,
                Email = l.Email,
                Phone = l.Phone,
                CompanyName = l.CompanyName,
                Source = l.Source,
                Status = l.Status,
                Priority = l.Priority,
                ExpectedValue = l.ExpectedValue,
                AssignedTo = l.AssignedTo,
                CreatedDate = l.CreatedDate,
                Notes = l.Notes
            })
            .ToListAsync();

        return Ok(leads);
    }

    // GET: /api/leads/5
    [HttpGet("{id:int}")]
    public async Task<ActionResult<LeadApiResponse>> GetLead(int id)
    {
        var lead = await AccessibleLeads()
            .Where(l => l.LeadId == id)
            .Select(l => new LeadApiResponse
            {
                LeadId = l.LeadId,
                LeadCode = l.LeadCode,
                LeadName = l.LeadName,
                Email = l.Email,
                Phone = l.Phone,
                CompanyName = l.CompanyName,
                Source = l.Source,
                Status = l.Status,
                Priority = l.Priority,
                ExpectedValue = l.ExpectedValue,
                AssignedTo = l.AssignedTo,
                CreatedDate = l.CreatedDate,
                Notes = l.Notes
            })
            .FirstOrDefaultAsync();

        if (lead == null)
        {
            return NotFound(new { message = "Lead not found." });
        }

        return Ok(lead);
    }

    // POST: /api/leads
    [HttpPost]
    public async Task<ActionResult<LeadApiResponse>> CreateLead(
        LeadCreateRequest request)
    {
        var lead = new Lead
        {
            LeadCode = $"LED-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            LeadName = request.LeadName.Trim(),
            Email = request.Email?.Trim(),
            Phone = request.Phone?.Trim(),
            CompanyName = request.CompanyName?.Trim(),
            Source = request.Source.Trim(),
            Status = request.Status,
            Priority = request.Priority,
            ExpectedValue = request.ExpectedValue,
            AssignedTo = CurrentUserId,
            CreatedDate = DateTime.UtcNow,
            Notes = request.Notes?.Trim()
        };

        var response = ToResponse(lead);

        _context.Leads.Add(lead);

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = CurrentUserId,
            Action = "Create",
            EntityName = "Lead",
            OldValue = null,
            NewValue = JsonSerializer.Serialize(response),
            CreatedDate = DateTime.UtcNow,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Result = "Success",
            Details = "Created lead through REST API."
        });

        await _context.SaveChangesAsync();

        response.LeadId = lead.LeadId;

        return CreatedAtAction(
            nameof(GetLead),
            new { id = lead.LeadId },
            response);
    }

    private static LeadApiResponse ToResponse(Lead lead)
    {
        return new LeadApiResponse
        {
            LeadId = lead.LeadId,
            LeadCode = lead.LeadCode,
            LeadName = lead.LeadName,
            Email = lead.Email,
            Phone = lead.Phone,
            CompanyName = lead.CompanyName,
            Source = lead.Source,
            Status = lead.Status,
            Priority = lead.Priority,
            ExpectedValue = lead.ExpectedValue,
            AssignedTo = lead.AssignedTo,
            CreatedDate = lead.CreatedDate,
            Notes = lead.Notes
        };
    }
}
