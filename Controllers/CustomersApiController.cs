
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
[Route("api/customers")]
[Authorize(Roles = "Admin,Manager,SalesExecutive")]
public class CustomersApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public CustomersApiController(ApplicationDbContext context)
    {
        _context = context;
    }

    private string CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private bool IsAdmin => User.IsInRole("Admin");

    private IQueryable<Customer> AccessibleCustomers()
    {
        var query = _context.Customers.AsQueryable();

        if (!IsAdmin)
        {
            query = query.Where(c => c.CreatedBy == CurrentUserId);
        }

        return query;
    }

    // GET: /api/customers
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CustomerApiResponse>>> GetCustomers(
        [FromQuery] string? search)
    {
        var query = AccessibleCustomers();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();

            query = query.Where(c =>
                c.CustomerName.Contains(search) ||
                c.CustomerCode.Contains(search) ||
                c.Email.Contains(search) ||
                c.Phone.Contains(search) ||
                (c.CompanyName != null && c.CompanyName.Contains(search)));
        }

        var customers = await query
            .OrderByDescending(c => c.CreatedDate)
            .Select(c => new CustomerApiResponse
            {
                CustomerId = c.CustomerId,
                CustomerCode = c.CustomerCode,
                CustomerName = c.CustomerName,
                Email = c.Email,
                Phone = c.Phone,
                CompanyName = c.CompanyName,
                Address = c.Address,
                City = c.City,
                State = c.State,
                Status = c.Status,
                CreatedDate = c.CreatedDate
            })
            .ToListAsync();

        return Ok(customers);
    }

    // GET: /api/customers/5
    [HttpGet("{id:int}")]
    public async Task<ActionResult<CustomerApiResponse>> GetCustomer(int id)
    {
        var customer = await AccessibleCustomers()
            .Where(c => c.CustomerId == id)
            .Select(c => new CustomerApiResponse
            {
                CustomerId = c.CustomerId,
                CustomerCode = c.CustomerCode,
                CustomerName = c.CustomerName,
                Email = c.Email,
                Phone = c.Phone,
                CompanyName = c.CompanyName,
                Address = c.Address,
                City = c.City,
                State = c.State,
                Status = c.Status,
                CreatedDate = c.CreatedDate
            })
            .FirstOrDefaultAsync();

        if (customer == null)
        {
            return NotFound(new { message = "Customer not found." });
        }

        return Ok(customer);
    }

    // POST: /api/customers
    [HttpPost]
    public async Task<ActionResult<CustomerApiResponse>> CreateCustomer(
        CustomerCreateRequest request)
    {
        var email = request.Email.Trim();
        var phone = request.Phone.Trim();

        var duplicate = await _context.Customers.AnyAsync(c =>
            c.Email.ToLower() == email.ToLower() ||
            c.Phone == phone);

        if (duplicate)
        {
            return Conflict(new
            {
                message = "A customer with this email or phone number already exists."
            });
        }

        var customer = new Customer
        {
            CustomerCode = $"CUS-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            CustomerName = request.CustomerName.Trim(),
            Email = email,
            Phone = phone,
            CompanyName = request.CompanyName?.Trim(),
            Address = request.Address?.Trim(),
            City = request.City?.Trim(),
            State = request.State?.Trim(),
            Status = request.Status,
            CreatedBy = CurrentUserId,
            CreatedDate = DateTime.UtcNow
        };

        var response = ToResponse(customer);

        _context.Customers.Add(customer);

        AddAuditLog(
            "Create",
            customer,
            null,
            response);

        await _context.SaveChangesAsync();

        response.CustomerId = customer.CustomerId;

        return CreatedAtAction(
            nameof(GetCustomer),
            new { id = customer.CustomerId },
            response);
    }

    // PUT: /api/customers/5
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateCustomer(
        int id,
        CustomerUpdateRequest request)
    {
        var customer = await AccessibleCustomers()
            .FirstOrDefaultAsync(c => c.CustomerId == id);

        if (customer == null)
        {
            return NotFound(new { message = "Customer not found." });
        }

        var email = request.Email.Trim();
        var phone = request.Phone.Trim();

        var duplicate = await _context.Customers.AnyAsync(c =>
            c.CustomerId != id &&
            (c.Email.ToLower() == email.ToLower() || c.Phone == phone));

        if (duplicate)
        {
            return Conflict(new
            {
                message = "Another customer already uses this email or phone number."
            });
        }

        var oldValue = ToResponse(customer);

        customer.CustomerName = request.CustomerName.Trim();
        customer.Email = email;
        customer.Phone = phone;
        customer.CompanyName = request.CompanyName?.Trim();
        customer.Address = request.Address?.Trim();
        customer.City = request.City?.Trim();
        customer.State = request.State?.Trim();
        customer.Status = request.Status;
        customer.ModifiedDate = DateTime.UtcNow;

        var newValue = ToResponse(customer);

        AddAuditLog(
            "Update",
            customer,
            oldValue,
            newValue);

        await _context.SaveChangesAsync();

        return NoContent();
    }

    // DELETE: /api/customers/5
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteCustomer(int id)
    {
        var customer = await AccessibleCustomers()
            .FirstOrDefaultAsync(c => c.CustomerId == id);

        if (customer == null)
        {
            return NotFound(new { message = "Customer not found." });
        }

        var hasRelatedRecords =
            await _context.Opportunities.AnyAsync(o => o.CustomerId == id) ||
            await _context.FollowUps.AnyAsync(f => f.CustomerId == id) ||
            await _context.Activities.AnyAsync(a => a.CustomerId == id);

        if (hasRelatedRecords)
        {
            return Conflict(new
            {
                message = "This customer cannot be deleted because related CRM records exist."
            });
        }

        var oldValue = ToResponse(customer);

        _context.Customers.Remove(customer);

        AddAuditLog(
            "Delete",
            customer,
            oldValue,
            null);

        await _context.SaveChangesAsync();

        return NoContent();
    }

    private static CustomerApiResponse ToResponse(Customer customer)
    {
        return new CustomerApiResponse
        {
            CustomerId = customer.CustomerId,
            CustomerCode = customer.CustomerCode,
            CustomerName = customer.CustomerName,
            Email = customer.Email,
            Phone = customer.Phone,
            CompanyName = customer.CompanyName,
            Address = customer.Address,
            City = customer.City,
            State = customer.State,
            Status = customer.Status,
            CreatedDate = customer.CreatedDate
        };
    }

    private void AddAuditLog(
        string action,
        Customer customer,
        object? oldValue,
        object? newValue)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = CurrentUserId,
            Action = action,
            EntityName = "Customer",
            RecordId = customer.CustomerId,
            OldValue = oldValue == null
                ? null
                : JsonSerializer.Serialize(oldValue),
            NewValue = newValue == null
                ? null
                : JsonSerializer.Serialize(newValue),
            CreatedDate = DateTime.UtcNow,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Result = "Success",
            Details = $"{action} customer through REST API."
        });
    }
}
