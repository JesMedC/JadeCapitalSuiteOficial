using FluentAssertions;
using JadeCapital.Identity.Application.Features.Auth.Consent;
using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Domain.Common;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Identity.UnitTests.Features.Auth.Consent;

public class ConsentHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILogger<ConsentHandler> _logger = Substitute.For<ILogger<ConsentHandler>>();

    public ConsentHandlerTests()
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success(1));
    }

    private ConsentHandler CreateSut() => new(_users, _uow, _clock, _logger);

    private static User BuildUser(UserStatus status = UserStatus.Active)
    {
        var registered = User.Register(
            Guid.NewGuid(),
            $"user-{Guid.NewGuid():N}@test.com",
            "Test User",
            "h",
            UserRole.Trader).Value;
        if (status != UserStatus.Active)
        {
            // Cancellation forces the user into Cancelled; tests that need
            // other statuses can layer additional transitions.
            registered.Cancel();
        }
        return registered;
    }

    [Fact]
    public async Task CookieChoice_All_Returns200AndUpdatesColumns()
    {
        var user = BuildUser();
        _users.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(user);

        var cmd = new ConsentCommand(user.Id, "all");
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Choice.Should().Be("all");
        user.CookieConsentChoice.Should().Be("all");
        user.CookieConsentAcceptedAt.Should().NotBeNull();
        await _users.Received(1).UpdateAsync(user, Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CookieChoice_Essential_Returns200AndUpdatesColumns()
    {
        var user = BuildUser();
        _users.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(user);

        var cmd = new ConsentCommand(user.Id, "ESSENTIAL");
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Choice.Should().Be("essential");
        user.CookieConsentChoice.Should().Be("essential");
        user.CookieConsentAcceptedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ReCall_SameChoice_IsIdempotent_PreservesOriginalTimestamp()
    {
        var user = BuildUser();
        var firstAt = new DateTimeOffset(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);
        user.RecordCookieConsent(firstAt, "all");
        var originalTimestamp = user.CookieConsentAcceptedAt;

        _users.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(user);
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero));

        var result = await CreateSut().Handle(new ConsentCommand(user.Id, "all"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.CookieConsentAcceptedAt.Should().Be(originalTimestamp,
            "idempotent re-call with the same choice MUST preserve the original timestamp.");
    }

    [Fact]
    public async Task Different_Choice_Tier_Change_UpdatesTimestamp()
    {
        var user = BuildUser();
        user.RecordCookieConsent(
            new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.Zero),
            "all");
        var stale = user.CookieConsentAcceptedAt;

        _users.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(user);
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));

        var result = await CreateSut().Handle(new ConsentCommand(user.Id, "essential"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.CookieConsentChoice.Should().Be("essential");
        user.CookieConsentAcceptedAt.Should().NotBe(stale);
    }

    [Fact]
    public async Task Unknown_User_Returns404()
    {
        _users.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ReturnsNull();

        var cmd = new ConsentCommand(Guid.NewGuid(), "all");
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.identity.user_not_found");
        await _uow.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task Invalid_Choice_Returns422()
    {
        var user = BuildUser();
        _users.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(user);

        var result = await CreateSut().Handle(new ConsentCommand(user.Id, "tracking-only"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.auth.cookie_consent_choice_invalid");
        await _users.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }
}
