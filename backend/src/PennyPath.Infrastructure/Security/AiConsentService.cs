using Microsoft.EntityFrameworkCore;
using PennyPath.Application.Security;
using PennyPath.Infrastructure.Persistence;

namespace PennyPath.Infrastructure.Security;

public class AiConsentService(AppDbContext db, TimeProvider clock) : IAiConsent
{
    public async Task<bool> IsEnabledAsync(Guid userId, CancellationToken ct = default) =>
        await db.Users.AnyAsync(u => u.Id == userId && u.AiConsentAt != null, ct);

    public async Task<bool> SetAsync(Guid userId, bool enabled, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return false;

        user.AiConsentAt = enabled ? user.AiConsentAt ?? clock.GetUtcNow().UtcDateTime : null;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
