using JadeCapital.Shared.Kernel.Time;
using Microsoft.Extensions.Options;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Identity.UnitTests.Features.Auth;

public class RefreshTokenHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refresh = Substitute.For<IRefreshTokenRepository>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IOptions<JwtOptions> _jwtOptions = Substitute.For<IOptions<JwtOptions>>();
    private readonly ILogger<RefreshTokenHandler> _logger = Substitute.For<ILogger<RefreshTokenHandler>>();

    public RefreshTokenHandlerTests()
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

    private RefreshTokenHandler CreateSut() => new(
        _users, _refresh, _tokens, _uow, _clock, _jwtOptions, _logger);

    [Fact]
    public async Task Handle_NotFound_ReturnsInvalid()
    {
        _tokens.HashToken(Arg.Any<string>()).Returns("h");
        _refresh.FindByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsNull();

        var cmd = new RefreshTokenCommand("opaque_token_xyz", null, null);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("unauthorized.auth.refresh_token_invalid");
    }

    [Fact]
    public async Task Handle_ReuseDetected_RevokesAllSessionsAndFails()
    {
        var fixedNow = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(fixedNow);
        var userId = Guid.NewGuid();
        var presented = RefreshToken.Issue(Guid.NewGuid(), userId, "h_presented", fixedNow.AddDays(-1), fixedNow.AddDays(13)).Value;
        // Forzamos estado revocado.
        presented.Revoke(fixedNow.AddMinutes(-5), Guid.NewGuid());

        _tokens.HashToken(Arg.Any<string>()).Returns("h_presented");
        _refresh.FindByHashAsync("h_presented", Arg.Any<CancellationToken>()).Returns(presented);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(JadeCapital.Shared.Kernel.Results.Result.Success(1));

        var cmd = new RefreshTokenCommand("opaque", null, null);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("unauthorized.auth.refresh_token_reuse_detected");
        await _refresh.Received(1).RevokeAllForUserAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExpiredToken_ReturnsExpired()
    {
        var fixedNow = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(fixedNow);
        var presented = RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "h_expired", fixedNow.AddDays(-30), fixedNow.AddMinutes(-1)).Value;

        _tokens.HashToken(Arg.Any<string>()).Returns("h_expired");
        _refresh.FindByHashAsync("h_expired", Arg.Any<CancellationToken>()).Returns(presented);

        var cmd = new RefreshTokenCommand("opaque", null, null);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("unauthorized.auth.refresh_token_expired");
    }

    [Fact]
    public async Task Handle_ValidToken_Rotates()
    {
        var fixedNow = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(fixedNow);
        var userId = Guid.NewGuid();
        var user = User.Register(userId, "user" + "@" + "test.com", "Name", "h", UserRole.Trader).Value;
        var presented = RefreshToken.Issue(Guid.NewGuid(), userId, "h_old", fixedNow.AddDays(-1), fixedNow.AddDays(13)).Value;

        _tokens.HashToken(Arg.Any<string>()).Returns("h_old");
        _refresh.FindByHashAsync("h_old", Arg.Any<CancellationToken>()).Returns(presented);
        _users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
        _tokens.CreateOpaqueRefreshToken().Returns("opaque_new");
        _tokens.HashToken("opaque_new").Returns("h_new");
        _tokens.CreateAccessToken(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>?>())
            .Returns(("new.jwt", fixedNow.AddMinutes(15)));
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(JadeCapital.Shared.Kernel.Results.Result.Success(1));

        var cmd = new RefreshTokenCommand("opaque_old", "1.1.1.1", "Mozilla/5.0");
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("new.jwt");
        result.Value.RefreshToken.Should().Be("opaque_new");
        presented.RevokedAt.Should().NotBeNull();
    }
}