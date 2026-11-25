using FixHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<OrderMessage> OrderMessages => Set<OrderMessage>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<PortfolioItem> PortfolioItems => Set<PortfolioItem>();
    public DbSet<FavoriteService> FavoriteServices => Set<FavoriteService>();
    public DbSet<FavoritePerformer> FavoritePerformers => Set<FavoritePerformer>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Complaint> Complaints => Set<Complaint>();
    public DbSet<VerificationDocument> VerificationDocuments => Set<VerificationDocument>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.PasswordHash).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Bio).HasMaxLength(2000);
            e.Property(x => x.Specialization).HasMaxLength(100);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.ServiceArea).HasMaxLength(200);
            e.Property(x => x.AvatarUrl).HasMaxLength(300);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);

            e.HasIndex(x => x.Email).IsUnique();
        });

        b.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Icon).HasMaxLength(40);
            e.Property(x => x.Color).HasMaxLength(7);

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

        b.Entity<OrderMessage>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(1000).IsRequired();

            e.HasOne(x => x.Order)
                .WithMany()
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Sender)
                .WithMany()
                .HasForeignKey(x => x.SenderId)
                .OnDelete(DeleteBehavior.Restrict);
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

        b.Entity<PortfolioItem>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.BeforeUrl).HasMaxLength(300);
            e.Property(x => x.AfterUrl).HasMaxLength(300).IsRequired();

            e.HasOne(x => x.Performer)
                .WithMany()
                .HasForeignKey(x => x.PerformerId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.PerformerId, x.CreatedAt });
        });

        b.Entity<FavoriteService>(e =>
        {
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Service)
                .WithMany()
                .HasForeignKey(x => x.ServiceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Одну услугу нельзя добавить в избранное дважды
            e.HasIndex(x => new { x.UserId, x.ServiceId }).IsUnique();
            e.HasIndex(x => x.ServiceId);
        });

        b.Entity<FavoritePerformer>(e =>
        {
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Performer)
                .WithMany()
                .HasForeignKey(x => x.PerformerId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.UserId, x.PerformerId }).IsUnique();
            e.HasIndex(x => x.PerformerId);
        });

        b.Entity<PasswordResetToken>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();

            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.TokenHash).IsUnique();
        });

        b.Entity<AuditLog>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(40).IsRequired();
            e.Property(x => x.TargetType).HasMaxLength(20).IsRequired();
            e.Property(x => x.TargetName).HasMaxLength(200);
            e.Property(x => x.Details).HasMaxLength(500);

            e.HasIndex(x => x.CreatedAt);

            e.HasOne(x => x.Admin)
                .WithMany()
                .HasForeignKey(x => x.AdminId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Complaint>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(20).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(30).IsRequired();
            e.Property(x => x.Comment).HasMaxLength(1000);
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.Property(x => x.Resolution).HasMaxLength(500);

            e.HasIndex(x => new { x.Status, x.CreatedAt });

            e.HasOne(x => x.Reporter)
                .WithMany()
                .HasForeignKey(x => x.ReporterId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<VerificationDocument>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(20).IsRequired();
            e.Property(x => x.OriginalName).HasMaxLength(200).IsRequired();
            e.Property(x => x.StoredName).HasMaxLength(80).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(100).IsRequired();

            e.HasIndex(x => x.UserId);

            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Notification>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(500).IsRequired();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);

            // список «мои уведомления» и счётчик непрочитанных ходят именно по этой паре
            e.HasIndex(x => new { x.UserId, x.IsRead });

            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}