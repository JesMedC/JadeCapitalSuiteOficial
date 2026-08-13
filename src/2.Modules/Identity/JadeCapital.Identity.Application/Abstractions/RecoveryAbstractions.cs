using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Application.Abstractions;

public interface ITemporaryCredentialRepository
{
    Task<Result<TemporaryCredential>> ReserveAsync(Guid id, Guid userId, int generation, string hash, CancellationToken ct = default);
    Task<int> LatestGenerationAsync(Guid userId, CancellationToken ct = default);
    Task<Result> ActivateAsync(Guid id, int expectedGeneration, CancellationToken ct = default);
    Task<TemporaryCredential?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<TemporaryCredential?> FindLatestActivatedAsync(Guid userId, CancellationToken ct = default);
    Task<TemporaryCredential?> FindByGrantJtiAsync(string grantJti, CancellationToken ct = default);
    Task<Result> ConsumeAsync(Guid id, string grantJti, DateTimeOffset utcNow, CancellationToken ct = default);
    Task<Result> ConfirmConsumedAsync(Guid id, DateTimeOffset utcNow, CancellationToken ct = default);
}
public interface IPasswordHistoryRepository { Task<Result> AppendAsync(Guid userId, string displacedHash, DateTimeOffset changedAt, CancellationToken ct = default); }
public interface IRefreshTokenRevoker { Task RevokeAllAsync(Guid userId, CancellationToken ct = default); }
public interface IDistributedLock { Task<IDistributedLockHandle> AcquireAsync(string key, CancellationToken ct = default); }
public interface IDistributedLockHandle : IAsyncDisposable { }
public interface IEmailSender { Task SendRecoveryEmailAsync(RecoveryEmailMessage message, CancellationToken ct = default); }
public sealed record RecoveryEmailMessage(string To, string DisplayName, string TemporaryPassword, DateTimeOffset ExpiresAt);
