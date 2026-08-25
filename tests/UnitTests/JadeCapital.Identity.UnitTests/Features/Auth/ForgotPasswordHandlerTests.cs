using JadeCapital.Identity.Application.Features.Recovery;
namespace JadeCapital.Identity.UnitTests.Features.Auth;

public class ForgotPasswordHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITemporaryCredentialRepository _temps = Substitute.For<ITemporaryCredentialRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly ILogger<ForgotPasswordHandler> _logger = Substitute.For<ILogger<ForgotPasswordHandler>>();

    private ForgotPasswordHandler Sut() => new(_users, _temps, _hasher, _email, _logger);

    [Fact]
    public async Task SendsOnlyCommittedCredentialWithPersistedExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var user = User.Register(Guid.NewGuid(), "trader@jade.test", "Joe", "current-hash", UserRole.Trader).Value;
        _users.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Hash(Arg.Any<string>()).Returns("hashed-temp");
        var credential = TemporaryCredential.Reserve(Guid.NewGuid(), user.Id, 1, CredentialHash.From("hashed-temp"), now).Value;
        credential.Activate(now, 1).IsSuccess.Should().BeTrue();
        _temps.IssueActivatedAsync(user.Id, "hashed-temp", Arg.Any<CancellationToken>()).Returns(Result.Success<TemporaryCredential?>(credential));

        var result = await Sut().Handle(new ForgotPasswordCommand("trader@jade.test"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Received.InOrder(() =>
        {
            _ = _temps.IssueActivatedAsync(user.Id, "hashed-temp", Arg.Any<CancellationToken>());
            _ = _email.SendRecoveryEmailAsync(Arg.Is<RecoveryEmailMessage>(m => m.To == user.Email
                && m.TemporaryPassword.Length == 26 && m.ExpiresAt == credential.ExpiresAt), Arg.Any<CancellationToken>());
        });
    }
}
