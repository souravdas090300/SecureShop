using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SecureShop.Domain.Entities;

namespace SecureShop.Infrastructure;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

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