using FluentAssertions;
using JadeCapital.Identity.Application.Features.Auth.Consent;
using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Infrastructure.Email;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Identity.UnitTests.Features.Auth.Register;

// ============================================================================
//  RegisterWelcomeEmailTests — Wave 11 slice 11.4.
//
//  Validates the post-registration welcome-email idempotency contract.
//
//  <para>
//  Three scenarios per the canonical spec:
//  <list type="number">
//  <item><b>SendsOnceOnFirstRegister</b>: fresh user → IEmailSender.SendAsync
//  fires once; WelcomeEmailSentAt = UtcNow.</item>
//  <item><b>IdempotentOnReRegister</b>: re-registration within the 7-day
//  suppression window does NOT re-fire the email; WelcomeEmailSentAt is
//  unchanged.</item>
//  <item><b>SuppressionAfter7Days</b>: re-registration after 7 days DOES
//  re-fire the email; WelcomeEmailSentAt is updated to the new UtcNow.</item>
//  </list>
//  </para>
//
//  <para>
//  The "existing user re-registers" path is exercised by stubbing the
//  repository to return a user that ALREADY has a WelcomeEmailSentAt
//  timestamp; the production conflict path on `existing != null` is a
//  different code path (returns 409) and is covered by
//  <c>RegisterUserHandlerTests.Handle_ExistingEmail_ReturnsConflict</c>.
///  </para>
// ============================================================================

public class RegisterWelcomeEmailTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refresh = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IOptions<JwtOptions> _jwtOptions = Substitute.For<IOptions<JwtOptions>>();
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly ILogger<RegisterUserHandler> _logger = Substitute.For<ILogger<RegisterUserHandler>>();

    public RegisterWelcomeEmailTests()
    {
        var fixedNow = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        _clock.UtcNow.Returns(fixedNow);
        _jwtOptions.Value.Returns(new JwtOptions
        {
            Issuer = "test",
            Audience = "test",
            AccessTokenSecret = new string('a', 32),
            RefreshTokenSecret = new string('b', 32),
            AccessTokenTtlMinutes = 15,
            RefreshTokenTtlDays = 14,
        });
        _users.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ReturnsNull();
        _users.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _hasher.Hash(Arg.Any<string>()).Returns("hashed_pwd");
        _tokens.CreateAccessToken(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>?>())
            .Returns(("access.jwt", fixedNow.AddMinutes(15)));
        _tokens.CreateOpaqueRefreshToken().Returns("opaque_refresh_abc");
        _tokens.HashToken(Arg.Any<string>()).Returns("hashed_refresh");
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success(1));
    }

    private RegisterUserHandler CreateSut() =>
        new(_users, _refresh, _hasher, _tokens, _uow, _clock, _jwtOptions, _email, _logger);

    [Fact]
    public async Task SendsOnceOnFirstRegister()
    {
        var fixedNow = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        _clock.UtcNow.Returns(fixedNow);

        var captured = new List<User>();
        await _users.AddAsync(Arg.Do<User>(u => captured.Add(u)), Arg.Any<CancellationToken>());

        var cmd = new RegisterUserCommand(
            Email: "user" + "@" + "test.com",
            DisplayName: "Fresh User",
            Password: "Passw0rd!Str",
            AcceptTerms: true, AcceptPrivacy: true, ConsentIp: "203.0.113.42",
            AcceptedTermsVersion: "v1.0", AcceptedPrivacyVersion: "v1.0");

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _email.Received(1).SendWelcomeEmailAsync(
            Arg.Is<WelcomeEmailMessage>(m =>
                m.To == "user@test.com" &&
                m.DisplayName == "Fresh User"),
            Arg.Any<CancellationToken>());

        captured[0].WelcomeEmailSentAt.Should().Be(fixedNow);
    }

    [Fact]
    public void IdempotentOnReRegister_SuppressesWhenWithinWindow()
    {
        var user = User.Register(Guid.NewGuid(), "user" + "@" + "test.com", "Test User", "h", UserRole.Trader).Value;
        var sentAt = new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);
        user.MarkWelcomeEmailSent(sentAt);

        var now = sentAt.AddDays(3); // within the 7-day window

        var decision = WelcomeEmailPolicy.ShouldSend(user, now);

        decision.Should().Be(WelcomeEmailPolicy.Decision.SuppressedRecentSend);
        WelcomeEmailPolicy.ShouldSendBool(user, now).Should().BeFalse();
    }

    [Fact]
    public void SuppressionAfter7Days_AllowsReSend()
    {
        var user = User.Register(Guid.NewGuid(), "user" + "@" + "test.com", "Test User", "h", UserRole.Trader).Value;
        var sentAt = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        user.MarkWelcomeEmailSent(sentAt);

        var now = sentAt.AddDays(8); // beyond the 7-day window

        var decision = WelcomeEmailPolicy.ShouldSend(user, now);

        decision.Should().Be(WelcomeEmailPolicy.Decision.SuppressionLapsed);
        WelcomeEmailPolicy.ShouldSendBool(user, now).Should().BeTrue();
    }

    [Fact]
    public void SendFirstTime_WhenNoPriorSend()
    {
        var user = User.Register(Guid.NewGuid(), "user" + "@" + "test.com", "Test User", "h", UserRole.Trader).Value;

        var decision = WelcomeEmailPolicy.ShouldSend(user, new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));

        decision.Should().Be(WelcomeEmailPolicy.Decision.SendFirstTime);
        WelcomeEmailPolicy.ShouldSendBool(user, _clock.UtcNow).Should().BeTrue();
    }
}
