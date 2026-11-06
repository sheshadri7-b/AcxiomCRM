
using AcxiomCRM.Models;
using Microsoft.AspNetCore.Identity;

namespace AcxiomCRM.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(
        IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var roleManager = scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole>>();

        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();

        // Create CRM roles if they do not exist.
        string[] roles =
        {
            "Admin",
            "Manager",
            "SalesExecutive"
        };

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(
                    new IdentityRole(role));

                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Could not create role {role}: " +
                        string.Join(", ",
                            result.Errors.Select(e => e.Description)));
                }
            }
        }

        // Assign SalesExecutive to existing users who have no CRM role.
        var users = userManager.Users.ToList();

        foreach (var user in users)
        {
            var userRoles = await userManager.GetRolesAsync(user);

            if (userRoles.Count == 0)
            {
                var result = await userManager.AddToRoleAsync(
                    user, "SalesExecutive");

                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Could not assign role to {user.Email}: " +
                        string.Join(", ",
                            result.Errors.Select(e => e.Description)));
                }
            }
        }
    }
}
