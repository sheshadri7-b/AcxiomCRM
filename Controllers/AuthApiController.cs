
using System.Security.Claims;
using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Models.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AcxiomCRM.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public AuthApiController(
        ApplicationDbContext context,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _signInManager = signInManager;
        _userManager = userManager;
    }

    // POST: api/auth/login
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<ApiLoginResponse>> Login(
        [FromBody] ApiLoginRequest request)
    {
        var email = request.Email.Trim();

        // Step 1: Find user by email.
        var user = await _userManager.FindByEmailAsync(email);

        if (user == null)
        {
            await WriteLoginAuditAsync(
                null,
                "Failure",
                "Login failed: invalid credentials.");

            return Unauthorized(new
            {
                message = "Invalid email or password."
            });
        }

        // Step 2: Check whether the account is active.
        if (!user.IsActive)
        {
            await WriteLoginAuditAsync(
                user.Id,
                "Failure",
                "Login rejected: inactive account.");

            return Unauthorized(new
            {
                message = "Invalid email or password."
            });
        }

        // Step 3: Verify password separately for diagnosis.
        var passwordValid = await _userManager.CheckPasswordAsync(
            user,
            request.Password);

        if (!passwordValid)
        {
            // Use PasswordSignInAsync so failed attempts
            // are counted and lockout policy is enforced.
            var failedResult = await _signInManager.PasswordSignInAsync(
                user,
                request.Password,
                request.RememberMe,
                lockoutOnFailure: true);

            var details = failedResult.IsLockedOut
                ? "Login rejected: account locked out."
                : "Login failed: invalid password.";

            await WriteLoginAuditAsync(
                user.Id,
                "Failure",
                details);

            return Unauthorized(new
            {
                message = "Invalid email or password."
            });
        }

        // Step 4: Check lockout status before successful sign-in.
        if (await _userManager.IsLockedOutAsync(user))
        {
            await WriteLoginAuditAsync(
                user.Id,
                "Failure",
                "Login rejected: account locked out.");

            return Unauthorized(new
            {
                message = "Account temporarily locked. Try again later."
            });
        }

        // Step 5: Sign in and issue authentication cookie.
        var signInResult = await _signInManager.PasswordSignInAsync(
            user,
            request.Password,
            request.RememberMe,
            lockoutOnFailure: false);

        if (!signInResult.Succeeded)
        {
            await WriteLoginAuditAsync(
                user.Id,
                "Failure",
                "Identity sign-in failed.");

            return Unauthorized(new
            {
                message = "Unable to complete login."
            });
        }

        // Step 6: Retrieve user roles.
        var roles = await _userManager.GetRolesAsync(user);

        // Step 7: Record successful login.
        await WriteLoginAuditAsync(
            user.Id,
            "Success",
            "User logged in through Authentication API.");

        return Ok(new ApiLoginResponse
        {
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            Roles = roles.ToList()
        });
    }

    // POST: api/auth/logout
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        await _signInManager.SignOutAsync();

        await WriteLoginAuditAsync(
            userId,
            "Success",
            "User logged out through Authentication API.");

        return Ok(new
        {
            message = "Logged out successfully."
        });
    }

    // Authentication audit logging
    private async Task WriteLoginAuditAsync(
        string? userId,
        string result,
        string details)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = result == "Success"
                ? "Login"
                : "LoginFailed",
            EntityName = "Authentication",
            RecordId = null,
            CreatedDate = DateTime.UtcNow,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Result = result,
            Details = details
        });

        await _context.SaveChangesAsync();
    }
}
