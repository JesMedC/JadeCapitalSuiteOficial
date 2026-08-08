using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.Extensions.Options;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Identity.UnitTests.Features.Auth;

public class LoginHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refresh = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IOptions<JwtOptions> _jwtOptions = Substitute.For<IOptions<JwtOptions>>();
    private readonly ILogger<LoginHandler> _logger = Substitute.For<ILogger<LoginHandler>>();

    public LoginHandlerTests()
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

    private LoginHandler CreateSut() => new(
        _users, _refresh, _hasher, _tokens, _uow, _clock, _jwtOptions, _logger);

    private static string Email() => "user" + "@" + "test.com";

    [Fact]
    public async Task Handle_WrongEmail_StillHashesAndReturnsUnauthorized()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _users.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ReturnsNull();
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success(0));

        var cmd = new LoginCommand(Email(), "anyPassword", null, null);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("unauthorized.auth.invalid_credentials");
        // Verify que se llamo al hasher para mantener timing constante.
        _hasher.Received(1).Verify(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_WrongPassword_RecordsFailedAndReturnsUnauthorized()
    {
        var user = User.Register(Guid.NewGuid(), Email(), "Name", "realhash", UserRole.Trader).Value;
        _users.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("badpassword", "realhash").Returns(false);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = new LoginCommand(Email(), "badpassword", null, null);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("unauthorized.auth.invalid_credentials");
        user.FailedLoginCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_CorrectPassword_ReturnsTokens()
    {
        var fixedNow = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(fixedNow);
        var user = User.Register(Guid.NewGuid(), Email(), "Name", "realhash", UserRole.Trader).Value;
        _users.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("goodpassword", "realhash").Returns(true);
        _tokens.CreateAccessToken(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>?>())
            .Returns(("access.jwt", fixedNow.AddMinutes(15)));
        _tokens.CreateOpaqueRefreshToken().Returns("opaque");
        _tokens.HashToken(Arg.Any<string>()).Returns("h");
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = new LoginCommand(Email(), "goodpassword", "1.1.1.1", "Mozilla/5.0");
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("access.jwt");
        result.Value.RefreshToken.Should().Be("opaque");
        user.LastLoginAt.Should().NotBeNull();
        user.LastLoginAt.Should().BeCloseTo(fixedNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Handle_AccountLockedOut_ReturnsForbidden()
    {
        var user = User.Register(Guid.NewGuid(), Email(), "Name", "realhash", UserRole.Trader).Value;
        for (var i = 0; i < User.MaxFailedLoginAttempts; i++) user.RecordFailedLogin();
        _users.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("goodpassword", "realhash").Returns(true);
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        var cmd = new LoginCommand(Email(), "goodpassword", null, null);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        // Successful login falla porque el usuario esta locked.
        result.Error.Code.Should().Be("forbidden.user.locked_out_cannot_login");
    }
}