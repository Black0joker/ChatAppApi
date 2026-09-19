using ChatApp.Application.Abstractions;
using ChatApp.Domain.Entities;
using ChatApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(ChatDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
        => db.Users.FirstOrDefaultAsync(x => x.Username == username, ct);

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
        => db.Users.FirstOrDefaultAsync(x => x.Email == email, ct);

    public Task<bool> ExistsByUsernameAsync(string username, CancellationToken ct = default)
        => db.Users.AnyAsync(x => x.Username == username, ct);

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default)
        => db.Users.AnyAsync(x => x.Email == email, ct);

    public async Task AddAsync(User user, CancellationToken ct = default)
        => await db.Users.AddAsync(user, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
