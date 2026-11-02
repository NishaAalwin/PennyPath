using Microsoft.EntityFrameworkCore;
using PennyPath.Domain.Entities;

namespace PennyPath.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Email).HasMaxLength(254).IsRequired();
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.PasswordHash).IsRequired();
        });

        b.Entity<Transaction>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Amount).HasPrecision(18, 2);
            e.Property(t => t.Merchant).HasMaxLength(200);
            e.Property(t => t.Category).HasMaxLength(100);
            e.Property(t => t.Account).HasMaxLength(20);
            e.Property(t => t.RawHash).HasMaxLength(64).IsRequired();
            e.Property(t => t.Type).HasConversion<string>().HasMaxLength(10);
            e.Property(t => t.Source).HasConversion<string>().HasMaxLength(10);
            e.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(t => new { t.UserId, t.RawHash }).IsUnique();
            e.HasIndex(t => new { t.UserId, t.Date });
        });
    }
}
