using JadeCapital.Identity.Application.Features.Recovery;
using JadeCapital.Shared.Kernel.Time;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Identity.UnitTests.Features.Auth;

/// <summary>Slice 0b test: <see cref="ForgotPasswordHandler"/> reserves a new
/// Pending generation, sends the email, and CAS-activates the row.</summary>
public class ForgotPasswordHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITemporaryCredentialRepository _temps = Substitute.For<ITemporaryCredentialRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ILogger<ForgotPasswordHandler> _logger = Substitute.For<ILogger<ForgotPasswordHandler>>();

    private ForgotPasswordHandler Sut() => new(_users, _temps, _hasher, _clock, _email, _uow, _logger);

    [Fact]
    public async Task ReservesSendsCASActivates()
    {
        var now = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(now);
        var user = User.Register(Guid.NewGuid(), "trader@jade.test", "Joe", "current-hash", UserRole.Trader).Value;
        _users.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Hash(Arg.Any<string>()).Returns("hashed-temp");
        var localNow = now;
        _temps.ReserveAsync(Arg.Any<Guid>(), user.Id, Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(args => TemporaryCredential.Reserve(args.ArgAt<Guid>(0), args.ArgAt<Guid>(1), args.ArgAt<int>(2),
                CredentialHash.From(args.ArgAt<string>(3)), localNow));
        _temps.LatestGenerationAsync(user.Id, Arg.Any<CancellationToken>()).Returns(0);
        _temps.ActivateAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var result = await Sut().Handle(new ForgotPasswordCommand("trader@jade.test"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _temps.Received(1).ReserveAsync(Arg.Any<Guid>(), user.Id, 1, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _email.Received(1).SendRecoveryEmailAsync(
            Arg.Is<RecoveryEmailMessage>(m => m.To == "trader@jade.test" && m.TemporaryPassword.Length == 26),
            Arg.Any<CancellationToken>());
        await _temps.Received(1).ActivateAsync(Arg.Any<Guid>(), 1, Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CallsSupersedeActiveAsync_BeforeReserveAsync()
    {
        var now = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(now);
        var user = User.Register(Guid.NewGuid(), "trader@jade.test", "Joe", "current-hash", UserRole.Trader).Value;
        _users.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Hash(Arg.Any<string>()).Returns("hashed-temp");
        _temps.LatestGenerationAsync(user.Id, Arg.Any<CancellationToken>()).Returns(0);
        _temps.ReserveAsync(Arg.Any<Guid>(), user.Id, Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(args => TemporaryCredential.Reserve(args.ArgAt<Guid>(0), args.ArgAt<Guid>(1), args.ArgAt<int>(2),
                CredentialHash.From(args.ArgAt<string>(3)), now));
        _temps.ActivateAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        await Sut().Handle(new ForgotPasswordCommand("trader@jade.test"), CancellationToken.None);

        Received.InOrder(async () =>
        {
            await _temps.SupersedeActiveAsync(user.Id, Arg.Any<CancellationToken>());
            await _temps.LatestGenerationAsync(user.Id, Arg.Any<CancellationToken>());
            await _temps.ReserveAsync(Arg.Any<Guid>(), user.Id, Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        });
    }
}
