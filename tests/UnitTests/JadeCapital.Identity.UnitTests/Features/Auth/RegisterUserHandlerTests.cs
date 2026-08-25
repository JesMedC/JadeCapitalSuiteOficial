using JadeCapital.Identity.Application.Features.Auth.Consent;
using JadeCapital.Shared.Infrastructure.Email;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.Extensions.Options;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Identity.UnitTests.Features.Auth;

public class RegisterUserHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refresh = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IOptions<JwtOptions> _jwtOptions = Substitute.For<IOptions<JwtOptions>>();
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly WelcomeEmailPolicy _welcomeEmailPolicy = new(Options.Create(new WelcomeEmailPolicyOptions()));
    private readonly ILogger<RegisterUserHandler> _logger = Substitute.For<ILogger<RegisterUserHandler>>();

    public RegisterUserHandlerTests()
    {
        _jwtOptions.Value.Returns(new JwtOptions
        {
            Issuer = "test",
            Audience = "test",
            AccessTokenSecret = new string('a', 32),
            RefreshTokenSecret = new string('b', 32),
            AccessTokenTtlMinutes = 15,
            RefreshTokenTtlDays = 14
        });
    }

    private RegisterUserHandler CreateSut() => new(
        _users, _refresh, _hasher, _tokens, _uow, _clock, _jwtOptions, _email, _welcomeEmailPolicy, _logger);

    private static string Email() => "user" + "@" + "test.com";

    [Fact]
    public async Task Handle_NewEmail_CreatesUserAndTokens()
    {
        var fixedNow = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(fixedNow);
        _users.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ReturnsNull();
        _hasher.Hash(Arg.Any<string>()).Returns("hashed_pwd");
        _tokens.CreateAccessToken(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IEnumerable<string>?>(),
                Arg.Is<TenantId>(id => id.Value == Guid.Parse("11111111-1111-1111-1111-111111111111")))
            .Returns(("access.jwt", fixedNow.AddMinutes(15)));
        _tokens.CreateOpaqueRefreshToken().Returns("opaque_refresh_abc");
        _tokens.HashToken(Arg.Any<string>()).Returns("hashed_refresh");
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success(1));

        var cmd = new RegisterUserCommand(
            Email(), "Valid Name", "Passw0rd!Str",
            AcceptTerms: true, AcceptPrivacy: true, ConsentIp: "203.0.113.42",
            AcceptedTermsVersion: "v1.0", AcceptedPrivacyVersion: "v1.0");
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Should().Be(Email());
        result.Value.AccessToken.Should().Be("access.jwt");
        result.Value.RefreshToken.Should().Be("opaque_refresh_abc");
        await _users.Received(1).AddAsync(
            Arg.Is<User>(u => u.Email == Email() && u.TenantId == new TenantId(Guid.Parse("11111111-1111-1111-1111-111111111111"))),
            Arg.Any<CancellationToken>());
        await _refresh.Received(1).AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
        await _uow.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());

        // The welcome email must fire on a fresh registration.
        await _email.Received(1).SendWelcomeEmailAsync(
            Arg.Is<WelcomeEmailMessage>(m => m.To == Email()),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingEmail_ReturnsConflict()
    {
        _users.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(User.Register(Guid.NewGuid(), Email(), "Existing", "h", UserRole.Trader).Value);

        var cmd = new RegisterUserCommand(
            Email(), "New Name", "Passw0rd!Str",
            AcceptTerms: true, AcceptPrivacy: true, ConsentIp: "203.0.113.42",
            AcceptedTermsVersion: "v1.0", AcceptedPrivacyVersion: "v1.0");
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.auth.email_already_registered");
        await _users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _email.DidNotReceiveWithAnyArgs().SendWelcomeEmailAsync(default!, default);
    }
}
