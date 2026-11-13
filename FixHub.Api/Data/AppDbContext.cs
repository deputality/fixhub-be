using FixHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<Review> Reviews => Set<Review>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.PasswordHash).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Bio).HasMaxLength(2000);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);

            e.HasIndex(x => x.Email).IsUnique();
        });

        b.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();

            e.HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.ParentId);
        });

        b.Entity<Service>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(4000).IsRequired();
            e.Property(x => x.PhotoUrl).HasMaxLength(500);
            e.Property(x => x.Price).HasPrecision(10, 2);
            e.Property(x => x.Unit).HasConversion<string>().HasMaxLength(20);

            e.HasOne(x => x.Performer)
                .WithMany()
                .HasForeignKey(x => x.PerformerId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Category)
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.CategoryId);
            e.HasIndex(x => x.Price);
            e.HasIndex(x => x.PerformerId);

            e.ToTable(t => t.HasCheckConstraint("CK_Services_Price", "\"Price\" >= 0"));
        });

        b.Entity<CartItem>(e =>
        {
            e.Property(x => x.Comment).HasMaxLength(1000);

            e.HasOne(x => x.Client)
                .WithMany()
                .HasForeignKey(x => x.ClientId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Service)
                .WithMany()
                .HasForeignKey(x => x.ServiceId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.ClientId, x.ServiceId }).IsUnique();

            e.ToTable(t => t.HasCheckConstraint("CK_CartItems_Quantity", "\"Quantity\" > 0"));
        });

        b.Entity<Order>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TotalAmount).HasPrecision(12, 2);
            e.Property(x => x.Address).HasMaxLength(500).IsRequired();
            e.Property(x => x.ContactName).HasMaxLength(200).IsRequired();
            e.Property(x => x.ContactPhone).HasMaxLength(30).IsRequired();

            e.HasOne(x => x.Client)
                .WithMany()
                .HasForeignKey(x => x.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Performer)
                .WithMany()
                .HasForeignKey(x => x.PerformerId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.ClientId, x.Status });
            e.HasIndex(x => new { x.PerformerId, x.Status });
        });

        b.Entity<OrderItem>(e =>
        {
            e.Property(x => x.ServiceTitle).HasMaxLength(200).IsRequired();
            e.Property(x => x.UnitPrice).HasPrecision(10, 2);
            e.Property(x => x.Comment).HasMaxLength(1000);

            e.HasOne(x => x.Order)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Service)
                .WithMany()
                .HasForeignKey(x => x.ServiceId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.OrderId);

            e.ToTable(t => t.HasCheckConstraint("CK_OrderItems_Quantity", "\"Quantity\" > 0"));
        });

        b.Entity<OrderStatusHistory>(e =>
        {
            e.ToTable("OrderStatusHistory");

            e.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20);

            e.HasOne(x => x.Order)
                .WithMany(x => x.History)
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.ChangedByUser)
                .WithMany()
                .HasForeignKey(x => x.ChangedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.OrderId);
        });

        b.Entity<Review>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(2000).IsRequired();

            // Один отзыв на одну позицию заказа
            e.HasIndex(x => x.OrderItemId).IsUnique();

            // Для расчёта рейтинга услуги и исполнителя
            e.HasIndex(x => new { x.ServiceId, x.IsHidden });
            e.HasIndex(x => new { x.PerformerId, x.IsHidden });
            e.HasIndex(x => x.ClientId);

            e.HasOne(x => x.OrderItem)
                .WithMany()
                .HasForeignKey(x => x.OrderItemId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Client)
                .WithMany()
                .HasForeignKey(x => x.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Service)
                .WithMany()
                .HasForeignKey(x => x.ServiceId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Performer)
                .WithMany()
                .HasForeignKey(x => x.PerformerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Оценка от 1 до 5
            e.ToTable(t => t.HasCheckConstraint("CK_Reviews_Rating", "\"Rating\" BETWEEN 1 AND 5"));
        });
    }
}