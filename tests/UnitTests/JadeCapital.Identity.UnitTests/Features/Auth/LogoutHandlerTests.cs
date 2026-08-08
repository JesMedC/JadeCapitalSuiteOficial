using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Identity.UnitTests.Features.Auth;

public class LogoutHandlerTests
{
    private readonly IRefreshTokenRepository _refresh = Substitute.For<IRefreshTokenRepository>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ILogger<LogoutHandler> _logger = Substitute.For<ILogger<LogoutHandler>>();

    private LogoutHandler CreateSut() => new(_refresh, _tokens, _uow, _logger);

    [Fact]
    public async Task Handle_EmptyRefreshToken_IdempotentSuccess()
    {
        var cmd = new LogoutCommand(Guid.NewGuid(), "");
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownToken_IdempotentSuccess()
    {
        _tokens.HashToken("unknown").Returns("h_unknown");
        _refresh.FindByHashAsync("h_unknown", Arg.Any<CancellationToken>()).ReturnsNull();

        var cmd = new LogoutCommand(Guid.NewGuid(), "unknown");
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidToken_RevokesAndSaves()
    {
        var fixedNow = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var token = JadeCapital.Identity.Domain.Authentication.RefreshToken
            .Issue(Guid.NewGuid(), userId, "h_real", fixedNow.AddDays(-1), fixedNow.AddDays(13)).Value;
        _tokens.HashToken("opaque").Returns("h_real");
        _refresh.FindByHashAsync("h_real", Arg.Any<CancellationToken>()).Returns(token);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(JadeCapital.Shared.Kernel.Results.Result.Success(1));

        var cmd = new LogoutCommand(userId, "opaque");
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        token.RevokedAt.Should().NotBeNull();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}