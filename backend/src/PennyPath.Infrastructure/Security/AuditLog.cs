using Microsoft.EntityFrameworkCore;
using PennyPath.Application.Security;
using PennyPath.Domain.Entities;
using PennyPath.Infrastructure.Persistence;

namespace PennyPath.Infrastructure.Security;

public class AuditLog(AppDbContext db, TimeProvider clock) : IAuditLog
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);
    private const int TrimBatch = 100;

    public async Task RecordAsync(Guid? userId, string type, string? ip, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        db.AuditEvents.Add(new AuditEvent
        {
            UserId = userId,
            Type = type,
            At = now,
            Ip = ip is { Length: > AuditEvent.MaxIpLength } ? ip[..AuditEvent.MaxIpLength] : ip,
        });

        var cutoff = now - Retention;
        db.AuditEvents.RemoveRange(await db.AuditEvents.Where(e => e.At < cutoff).Take(TrimBatch).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEventDto>> RecentAsync(Guid userId, int take = 50, CancellationToken ct = default) =>
        await db.AuditEvents.Where(e => e.UserId == userId)
            .OrderByDescending(e => e.At).Take(Math.Clamp(take, 1, 200))
            .Select(e => new AuditEventDto(e.Id, e.Type, e.At, e.Ip))
            .ToListAsync(ct);
}
