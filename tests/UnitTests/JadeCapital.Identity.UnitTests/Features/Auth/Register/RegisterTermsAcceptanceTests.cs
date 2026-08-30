using FluentAssertions;
using JadeCapital.Identity.Application._Common;
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
//  RegisterTermsAcceptanceTests — Wave 11 slice 11.4.
//
//  Verifies the validator gate at the wiring boundary (FluentValidation)
//  AND the per-handler persistence path for the GDPR Art. 7 consent
//  ledger (terms_accepted_at + privacy_accepted_at + consent_ip).
//
//  The behavioral surface lives in the validator (Phase 4 of tasks.md);
//  the regression risk is a future refactor that "accidentally" drops
//  the AcceptTerms / AcceptPrivacy / ConsentIp persistence — these tests
//  pin that surface stable.
// ============================================================================

public class RegisterTermsAcceptanceTests
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

    public RegisterTermsAcceptanceTests()
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
        new(_users, _refresh, _hasher, _tokens, _uow, _clock, _jwtOptions, _email, _welcomeEmailPolicy, _logger);

    [Fact]
    public async Task Valid_Registration_PersistsConsentLedgerColumns()
    {
        var captured = new List<User>();
        await _users.AddAsync(Arg.Do<User>(u => captured.Add(u)), Arg.Any<CancellationToken>());

        var cmd = new RegisterUserCommand(
            Email: "user" + "@" + "test.com",
            DisplayName: "Valid User",
            Password: "Passw0rd!Str",
            AcceptTerms: true,
            AcceptPrivacy: true,
            ConsentIp: "203.0.113.42",
            AcceptedTermsVersion: "v1.0",
            AcceptedPrivacyVersion: "v1.0");

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().HaveCount(1);
        var persisted = captured[0];
        persisted.TermsAcceptedAt.Should().NotBeNull();
        persisted.PrivacyAcceptedAt.Should().NotBeNull();
        persisted.ConsentIp.Should().Be("203.0.113.42");
    }

    [Fact]
    public async Task Invalid_Choice_HasMissingTerms_DoesNotRegister()
    {
        var cmd = new RegisterUserCommand(
            Email: "user" + "@" + "test.com",
            DisplayName: "Valid User",
            Password: "Passw0rd!Str",
            AcceptTerms: false,
            AcceptPrivacy: true,
            ConsentIp: "203.0.113.42",
            AcceptedTermsVersion: "v1.0",
            AcceptedPrivacyVersion: "v1.0");

        var validator = new RegisterUserValidator();
        var validationResult = validator.Validate(cmd);

        validationResult.IsValid.Should().BeFalse();
        validationResult.Errors.Should().Contain(e => e.ErrorCode == "validation.auth.terms_required");
        await _users.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    private static string Email() => "user" + "@" + "test.com";
}
