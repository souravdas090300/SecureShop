using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using SecureShop.Domain.Entities;

namespace SecureShop.API.Helpers;

public sealed record BootstrapAdminCredentials(string Email, string Password);

public static class BootstrapAdminProvisioner
{
    public static BootstrapAdminCredentials? GetCredentials(IConfiguration configuration)
    {
        var email = configuration["BootstrapAdmin:Email"];
        var password = configuration["BootstrapAdmin:Password"];
        var hasEmail = !string.IsNullOrWhiteSpace(email);
        var hasPassword = !string.IsNullOrWhiteSpace(password);

        if (hasEmail != hasPassword)
        {
            throw new InvalidOperationException(
                "BootstrapAdmin:Email and BootstrapAdmin:Password must both be configured to provision an initial administrator.");
        }

        return hasEmail ? new BootstrapAdminCredentials(email!, password!) : null;
    }

    public static async Task ProvisionIfNeededAsync(
        UserManager<ApplicationUser> userManager,
        BootstrapAdminCredentials? credentials,
        ILogger logger)
    {
        if (credentials is null)
        {
            logger.LogInformation("Initial administrator bootstrap is not configured; no administrator was created.");
            return;
        }

        if ((await userManager.GetUsersInRoleAsync("Admin")).Count > 0)
        {
            logger.LogInformation("An administrator already exists; initial administrator bootstrap was skipped.");
            return;
        }

        if (await userManager.FindByEmailAsync(credentials.Email) is not null)
        {
            throw new InvalidOperationException(
                "The configured bootstrap administrator email already belongs to a non-admin account. No role changes were made.");
        }

        var user = new ApplicationUser
        {
            UserName = credentials.Email,
            Email = credentials.Email,
            FirstName = "Admin",
            LastName = "User",
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(user, credentials.Password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create the configured initial administrator: {FormatErrors(createResult)}");
        }

        var roleResult = await userManager.AddToRoleAsync(user, "Admin");
        if (!roleResult.Succeeded)
        {
            var deleteResult = await userManager.DeleteAsync(user);
            if (!deleteResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"The initial administrator could not be assigned the Admin role and its account could not be removed. Role errors: {FormatErrors(roleResult)}; cleanup errors: {FormatErrors(deleteResult)}");
            }

            throw new InvalidOperationException(
                $"The initial administrator could not be assigned the Admin role; its account was removed: {FormatErrors(roleResult)}");
        }

        logger.LogInformation("Created the initial administrator from temporary bootstrap configuration.");
    }

    private static string FormatErrors(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(error => error.Description));
}
