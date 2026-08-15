using JadeCapital.Identity.Application.Features.Recovery;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.Extensions.Options;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Identity.UnitTests.Features.Auth;

public class ChangePasswordHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITemporaryCredentialRepository _temps = Substitute.For<ITemporaryCredentialRepository>();
    private readonly IRefreshTokenRevoker _revoker = Substitute.For<IRefreshTokenRevoker>();
    private readonly IPasswordHistoryRepository _history = Substitute.For<IPasswordHistoryRepository>();
    private readonly IDistributedLock _locks = Substitute.For<IDistributedLock>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IPasswordChangeReuseChecker _reuse = Substitute.For<IPasswordChangeReuseChecker>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();
    private readonly IRefreshTokenRepository _refresh = Substitute.For<IRefreshTokenRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IOptions<JwtOptions> _jwt = Substitute.For<IOptions<JwtOptions>>();

    public ChangePasswordHandlerTests()
    {
        _jwt.Value.Returns(new JwtOptions { Issuer = "test", Audience = "test", AccessTokenSecret = new string('a', 32), RefreshTokenSecret = new string('b', 32), AccessTokenTtlMinutes = 15, RefreshTokenTtlDays = 14 });
        _locks.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => new NoopLock());
    }

    private LoginWithTemporaryHandler LoginSut() => new(_users, _temps, _hasher, _tokens, _uow, _clock, Substitute.For<ILogger<LoginWithTemporaryHandler>>());
    private ChangePasswordWithGrantHandler GrantSut() => new(_users, _temps, _revoker, _refresh, _history, _locks, _reuse, _hasher, _tokens, _uow, _clock, _jwt, Substitute.For<ILogger<ChangePasswordWithGrantHandler>>());
    private ChangePasswordVoluntaryHandler VolSut() => new(_users, _revoker, _refresh, _locks, _reuse, _hasher, _tokens, _uow, _clock, _jwt, Substitute.For<ILogger<ChangePasswordVoluntaryHandler>>());

    private static User RegisterUser(Guid id, string hash) => User.Register(id, "trader@jade.test", "Joe", hash, UserRole.Trader).Value;
    private static TemporaryCredential Activated(Guid userId, int gen, DateTimeOffset now)
    { var c = TemporaryCredential.Reserve(Guid.NewGuid(), userId, gen, CredentialHash.From("temp-hash"), now).Value; c.Activate(now, gen); return c; }
    private static void StubTokens(ITokenService t, Guid userId, DateTimeOffset now, string access = "u.jwt", string refresh = "opq")
    { t.CreateAccessToken(userId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>?>()).Returns((access, now.AddMinutes(15))); t.CreateOpaqueRefreshToken().Returns(refresh); t.HashToken(refresh).Returns("h"); }

    [Fact]
    public async Task LoginWithTemp_MarksUsedIssuesGrantJti()
    {
        var now = DateTimeOffset.UtcNow; _clock.UtcNow.Returns(now);
        var userId = Guid.NewGuid(); var user = RegisterUser(userId, "current-hash");
        var cred = Activated(userId, 1, now);
        _users.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("TEMPPLAIN", "temp-hash").Returns(true);
        _temps.FindLatestActivatedAsync(user.Id, Arg.Any<CancellationToken>()).Returns(cred);
        _temps.ConsumeAsync(cred.Id, Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));
        _tokens.CreateAccessToken(userId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>?>())
            .Returns(call => { call.ArgAt<IEnumerable<string>?>(3).Should().Contain(c => c.StartsWith("grant_jti=")); return ("restricted.jwt", now.AddMinutes(15)); });
        var result = await LoginSut().Handle(new LoginWithTemporaryCommand("trader@jade.test", "TEMPPLAIN", null, null), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("restricted.jwt");
        result.Value.RefreshToken.Should().BeNull();
        await _temps.Received(1).ConsumeAsync(cred.Id, Arg.Any<string>(), now, Arg.Any<CancellationToken>());
        user.FailedLoginCount.Should().Be(0);
    }

    [Fact]
    public async Task ChangePassword_ConsumesGrantRevokesIssuesUnrestricted()
    {
        var now = DateTimeOffset.UtcNow; _clock.UtcNow.Returns(now);
        var userId = Guid.NewGuid(); var user = RegisterUser(userId, "old-hash");
        var ver = user.SessionVersion;
        var cred = Activated(userId, 1, now); cred.MarkConsumed(now, "grant-jti-001");
        _users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
        _temps.FindByGrantJtiAsync("grant-jti-001", Arg.Any<CancellationToken>()).Returns(cred);
        _temps.ConfirmConsumedAsync(cred.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _history.AppendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _reuse.IsReused(Arg.Any<User>(), Arg.Any<string>()).Returns(false);
        _hasher.Hash("NewPass9876!").Returns("new-hash");
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        StubTokens(_tokens, userId, now);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));
        var result = await GrantSut().Handle(new ChangePasswordWithGrantCommand(userId, "grant-jti-001", ver, "NewPass9876!", null, null), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("u.jwt");
        result.Value.RefreshToken.Should().Be("opq");
        await _revoker.Received(1).RevokeAllAsync(userId, Arg.Any<CancellationToken>());
        await _refresh.Received(1).AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
        user.SessionVersion.Should().Be(ver + 1);
    }

    [Fact]
    public async Task GrantJtiReplayRejected()
    {
        var now = DateTimeOffset.UtcNow; _clock.UtcNow.Returns(now);
        var userId = Guid.NewGuid(); var user = RegisterUser(userId, "old-hash");
        var cred = Activated(userId, 1, now); cred.MarkConsumed(now, "grant-jti-001");
        _users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
        _temps.FindByGrantJtiAsync("grant-jti-001", Arg.Any<CancellationToken>()).Returns(cred);
        _temps.ConfirmConsumedAsync(cred.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Result.Failure(IdentityApplicationErrors.Auth.RecoveryInvalid));
        _hasher.Hash(Arg.Any<string>()).Returns("new-hash");
        _reuse.IsReused(Arg.Any<User>(), Arg.Any<string>()).Returns(false);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(0));
        var result = await GrantSut().Handle(new ChangePasswordWithGrantCommand(userId, "grant-jti-001", 0, "NewPass9876!", null, null), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("unauthorized.");
        _tokens.DidNotReceive().CreateAccessToken(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>?>());
        await _refresh.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConcurrentChangeLoserFailsAtomically()
    {
        var now = DateTimeOffset.UtcNow; _clock.UtcNow.Returns(now);
        var userId = Guid.NewGuid(); var user = RegisterUser(userId, "old-hash");
        var ver = user.SessionVersion;
        var gate = new SemaphoreSlim(1, 1);
        _locks.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async call => { await gate.WaitAsync(call.ArgAt<CancellationToken>(1)).ConfigureAwait(false); return (IDistributedLockHandle)new SemaphoreHandle(gate); });
        _users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
        _reuse.IsReused(Arg.Any<User>(), Arg.Any<string>()).Returns(false);
        _hasher.Hash(Arg.Any<string>()).Returns("new-hash");
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        StubTokens(_tokens, userId, now);
        _revoker.RevokeAllAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));
        var sut = VolSut();
        var results = await Task.WhenAll(
            sut.Handle(new ChangePasswordVoluntaryCommand(userId, ver, "CurrentPass1!", "NewPass9876!", null, null), CancellationToken.None),
            sut.Handle(new ChangePasswordVoluntaryCommand(userId, ver, "CurrentPass1!", "OtherPass4321!", null, null), CancellationToken.None));
        results.Count(r => r.IsSuccess).Should().Be(1);
        results.Count(r => r.IsFailure).Should().Be(1);
        user.SessionVersion.Should().Be(ver + 1);
        results.Single(r => r.IsFailure).Error.Code.Should().Contain("concurrent_update");
    }

    [Fact]
    public async Task ChangeWithoutGrantReturnsRecoveryInvalid()
    {
        var now = DateTimeOffset.UtcNow; _clock.UtcNow.Returns(now);
        var userId = Guid.NewGuid(); var user = RegisterUser(userId, "old-hash");
        _users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
        _temps.FindByGrantJtiAsync("not-a-real-grant", Arg.Any<CancellationToken>()).ReturnsNull();
        var result = await GrantSut().Handle(new ChangePasswordWithGrantCommand(userId, "not-a-real-grant", 0, "NewPass9876!", null, null), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("unauthorized.");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _revoker.DidNotReceive().RevokeAllAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _refresh.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    private sealed class NoopLock : IDistributedLockHandle { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    private sealed class SemaphoreHandle : IDistributedLockHandle
    {
        private readonly SemaphoreSlim _gate; private bool _disposed;
        public SemaphoreHandle(SemaphoreSlim gate) => _gate = gate;
        public ValueTask DisposeAsync() { if (_disposed) return ValueTask.CompletedTask; _gate.Release(); _disposed = true; return ValueTask.CompletedTask; }
    }
}
