using ChatApp.Domain.Entities;

namespace ChatApp.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>
    /// Batch fetch by id (single round-trip). Prevents N+1 when enriching
    /// member lists — see ConversationService.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, User>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

    /// <summary>
    /// Stamp LastSeen without loading entities (single UPDATE; works regardless
    /// of change tracking — GetByIdsAsync is AsNoTracking by design).
    /// </summary>
    /// <returns>Number of rows updated.</returns>
    Task<int> UpdateLastSeenAsync(IEnumerable<Guid> userIds, DateTimeOffset when, CancellationToken ct = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<bool> ExistsByUsernameAsync(string username, CancellationToken ct = default);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
