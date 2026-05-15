using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SecureShop.Domain.Entities;

namespace SecureShop.Infrastructure;

/// <summary>
/// EF Core database context for SecureShop.
/// Extends <see cref="IdentityDbContext{TUser}"/> to include ASP.NET Core Identity tables
/// and implements <see cref="IDataProtectionKeyContext"/> so that Data Protection keys are
/// persisted in PostgreSQL (instead of the file system) for stateless Railway deployments.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser>, IDataProtectionKeyContext
{
    /// <summary>Initialises the context with the given EF Core options.</summary>
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    /// <summary>Data Protection key storage table (used for cookie/form-token encryption).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    /// <summary>Product catalogue.</summary>
    public DbSet<Product> Products => Set<Product>();
    /// <summary>Customer orders.</summary>
    public DbSet<Order> Orders => Set<Order>();
    /// <summary>Individual line items within orders.</summary>
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    /// <summary>
    /// Configures entity mappings, precision settings, relationships, and global query filters.
    /// A global <c>IsActive</c> filter on <see cref="Product"/> means soft-deleted products
    /// are invisible to all LINQ queries by default.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Product>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Price).HasPrecision(18, 2);
            e.Property(p => p.Name).HasMaxLength(200).IsRequired();
            e.Property(p => p.Category).HasMaxLength(100).IsRequired();
            e.HasQueryFilter(p => p.IsActive);
        });

        builder.Entity<Order>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.TotalAmount).HasPrecision(18, 2);
            e.HasOne(o => o.User).WithMany(u => u.Orders).HasForeignKey(o => o.UserId);
        });

        builder.Entity<OrderItem>(e =>
        {
            e.HasKey(oi => oi.Id);
            e.Property(oi => oi.UnitPrice).HasPrecision(18, 2);
            e.HasOne(oi => oi.Product)
                .WithMany()
                .HasForeignKey(oi => oi.ProductId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}