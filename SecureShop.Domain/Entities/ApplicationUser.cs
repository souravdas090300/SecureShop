using Microsoft.AspNetCore.Identity;

namespace SecureShop.Domain.Entities;

public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}