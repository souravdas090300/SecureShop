using Microsoft.AspNetCore.Identity;

namespace SecureShop.Domain.Entities;

/// <summary>
/// Extends ASP.NET Core Identity's <see cref="IdentityUser"/> with store-specific
/// profile fields and a navigation property to the user's orders.
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>The user's given (first) name.</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>The user's family (last) name.</summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>UTC timestamp recording when the account was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>All orders placed by this user.</summary>
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}