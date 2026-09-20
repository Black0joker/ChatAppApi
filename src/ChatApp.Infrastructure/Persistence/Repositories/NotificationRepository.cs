using ChatApp.Application.Abstractions;
using ChatApp.Domain.Entities;
using ChatApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Infrastructure.Persistence.Repositories;

public sealed class NotificationRepository(ChatDbContext db) : INotificationRepository
{
    public Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Notifications.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Notification>> GetForUserAsync(Guid userId, int limit, CancellationToken ct = default)
        => await db.Notifications
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(limit)
            .ToListAsync(ct);

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default)
        => db.Notifications.CountAsync(x => x.UserId == userId && x.ReadAt == null, ct);

    public async Task AddAsync(Notification notification, CancellationToken ct = default)
        => await db.Notifications.AddAsync(notification, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
