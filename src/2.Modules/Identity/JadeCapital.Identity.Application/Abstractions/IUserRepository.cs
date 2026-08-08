using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string email, CancellationToken ct);
    Task<User?> FindByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(User user, CancellationToken ct);
    Task UpdateAsync(User user, CancellationToken ct);
}

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token, CancellationToken ct);
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct);
    Task<IReadOnlyList<RefreshToken>> ListActiveByUserIdAsync(Guid userId, CancellationToken ct);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct);
}