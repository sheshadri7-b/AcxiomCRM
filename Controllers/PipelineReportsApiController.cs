
using System.Security.Claims;
using AcxiomCRM.Data;
using AcxiomCRM.Models.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = "Admin,Manager,SalesExecutive")]
public class PipelineReportsApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public PipelineReportsApiController(ApplicationDbContext context)
    {
        _context = context;
    }

    private string CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private bool IsAdmin => User.IsInRole("Admin");

    // GET: /api/reports/pipeline
    [HttpGet("pipeline")]
    public async Task<ActionResult<PipelineReportApiResponse>> GetPipelineReport()
    {
        var query = _context.Opportunities
            .Where(o => o.Status == "Open");

        // Admin can view all open opportunities.
        // Other roles currently see their assigned opportunities.
        if (!IsAdmin)
        {
            query = query.Where(o => o.AssignedTo == CurrentUserId);
        }

        var totalOpenOpportunities = await query.CountAsync();

        var totalPipelineValue = await query
            .SumAsync(o => (decimal?)o.Amount) ?? 0m;

        var weightedPipelineValue = await query
            .SumAsync(o =>
                (decimal?)(o.Amount * o.Probability / 100m)) ?? 0m;

        var stageBreakdown = await query
            .GroupBy(o => o.Stage)
            .Select(group => new PipelineStageRow
            {
                Stage = group.Key,
                OpportunityCount = group.Count(),
                PipelineValue = group.Sum(o => o.Amount),
                WeightedPipelineValue = group.Sum(
                    o => o.Amount * o.Probability / 100m)
            })
            .OrderBy(row => row.Stage)
            .ToListAsync();

        var ownerBreakdown = await query
            .GroupBy(o => o.AssignedTo)
            .Select(group => new PipelineOwnerRow
            {
                AssignedTo = group.Key,
                OpportunityCount = group.Count(),
                PipelineValue = group.Sum(o => o.Amount),
                WeightedPipelineValue = group.Sum(
                    o => o.Amount * o.Probability / 100m)
            })
            .OrderByDescending(row => row.PipelineValue)
            .ToListAsync();

        var response = new PipelineReportApiResponse
        {
            TotalOpenOpportunities = totalOpenOpportunities,
            TotalPipelineValue = totalPipelineValue,
            WeightedPipelineValue = weightedPipelineValue,
            StageBreakdown = stageBreakdown,
            OwnerBreakdown = ownerBreakdown
        };

        return Ok(response);
    }
}
