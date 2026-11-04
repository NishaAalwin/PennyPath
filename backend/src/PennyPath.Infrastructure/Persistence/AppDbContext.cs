using Microsoft.EntityFrameworkCore;
using PennyPath.Domain.Entities;

namespace PennyPath.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<GmailConnection> GmailConnections => Set<GmailConnection>();
    public DbSet<GmailOAuthState> GmailOAuthStates => Set<GmailOAuthState>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Email).HasMaxLength(254).IsRequired();
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.PasswordHash).IsRequired();
        });

        b.Entity<Session>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.UserId);
        });

        b.Entity<AuditEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(40).IsRequired();
            e.Property(x => x.Ip).HasMaxLength(AuditEvent.MaxIpLength);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.At });
            e.HasIndex(x => x.At);
        });

        b.Entity<GmailConnection>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ProtectedRefreshToken).IsRequired();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.UserId).IsUnique(); // one Gmail account per user
        });

        b.Entity<GmailOAuthState>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.StateHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.ProtectedCodeVerifier).IsRequired();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.StateHash).IsUnique();
        });

        b.Entity<Transaction>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Amount).HasPrecision(18, 2);
            e.Property(t => t.Merchant).HasMaxLength(200);
            e.Property(t => t.Category).HasMaxLength(100);
            e.Property(t => t.RawHash).HasMaxLength(64).IsRequired();
            e.Property(t => t.Type).HasConversion<string>().HasMaxLength(10);
            e.Property(t => t.Source).HasConversion<string>().HasMaxLength(10);
            e.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(t => new { t.UserId, t.RawHash }).IsUnique();
            e.HasIndex(t => new { t.UserId, t.Date });
            e.HasIndex(t => new { t.UserId, t.Amount, t.Date });
        });

    }
}
