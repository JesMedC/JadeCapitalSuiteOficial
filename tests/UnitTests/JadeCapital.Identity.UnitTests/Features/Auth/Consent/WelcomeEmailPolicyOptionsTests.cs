using FluentAssertions;
using JadeCapital.Identity.Application.Features.Auth.Consent;
using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Domain.Users;
using Microsoft.Extensions.Options;

namespace JadeCapital.Identity.UnitTests.Features.Auth.Consent;

// ============================================================================
//  WelcomeEmailPolicyOptionsTests — Wave 12 slice 12.2.
//
//  Validates the per-environment tunability extracted from the previously
//  hard-coded 7-day suppression window. The behavior is split across:
//    - WelcomeEmailPolicyOptions (POCO bound from appsettings:WelcomeEmailPolicy).
//    - WelcomeEmailPolicy (DI-injected instance that wraps the options).
//
//  <para>
//  Defaults are intentional production values: 7-day suppression + SendOnRegister=true
//  matches Wave 11.4 behavior. The validator (registered in IdentityModuleRegistration)
//  rejects SuppressionDays < 0 at host start via ValidateOnStart.
//  </para>
// ============================================================================

public class WelcomeEmailPolicyOptionsTests
{
    [Fact]
    public void Defaults_AreProductionSafe()
    {
        var opts = new WelcomeEmailPolicyOptions();

        opts.SuppressionDays.Should().Be(7);
        opts.SendOnRegister.Should().BeTrue();
        opts.Suppression.Should().Be(TimeSpan.FromDays(7));
    }

    [Fact]
    public void SectionName_IsStable()
    {
        WelcomeEmailPolicyOptions.SectionName.Should().Be("WelcomeEmailPolicy");
    }

    [Fact]
    public void Suppression_ReflectsSuppressionDays()
    {
        var opts = new WelcomeEmailPolicyOptions { SuppressionDays = 14 };

        opts.Suppression.Should().Be(TimeSpan.FromDays(14));
    }

    [Fact]
    public void ZeroDays_IsAllowed_DoesNotSuppress()
    {
        var opts = new WelcomeEmailPolicyOptions { SuppressionDays = 0 };

        opts.Suppression.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void NegativeDays_BypassesDefaultButFailsValidator()
    {
        // Constructor does NOT clamp — the IValidateOptions in DI does.
        var opts = new WelcomeEmailPolicyOptions { SuppressionDays = -1 };

        opts.SuppressionDays.Should().Be(-1);
        opts.Suppression.Should().Be(TimeSpan.FromDays(-1));
    }

    [Fact]
    public void SendOnRegister_False_DisablesWelcomeEmails()
    {
        var opts = new WelcomeEmailPolicyOptions { SendOnRegister = false };

        opts.SendOnRegister.Should().BeFalse();
    }
}

public class WelcomeEmailPolicyInstanceTests
{
    private static WelcomeEmailPolicy CreatePolicy(int suppressionDays = 7, bool sendOnRegister = true)
        => new(Options.Create(new WelcomeEmailPolicyOptions
        {
            SuppressionDays = suppressionDays,
            SendOnRegister = sendOnRegister,
        }));

    private static User NewUser()
        => User.Register(Guid.NewGuid(), "u" + "@" + "test.com", "Test User", "h", UserRole.Trader).Value;

    [Fact]
    public void IsEnabled_Property_HonorsSendOnRegister()
    {
        CreatePolicy(sendOnRegister: false).IsEnabled.Should().BeFalse();
        CreatePolicy(sendOnRegister: true).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void SuppressionWindow_Property_ReflectsOptions()
    {
        CreatePolicy(suppressionDays: 14).SuppressionWindow.Should().Be(TimeSpan.FromDays(14));
        CreatePolicy(suppressionDays: 7).SuppressionWindow.Should().Be(TimeSpan.FromDays(7));
    }

    [Fact]
    public void ShouldSend_SendFirstTime_WhenNoPriorSend()
    {
        var policy = CreatePolicy();
        var user = NewUser();

        var decision = policy.ShouldSend(user, new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));

        decision.Should().Be(WelcomeEmailPolicy.Decision.SendFirstTime);
    }

    [Fact]
    public void ShouldSend_SuppressedRecentSend_WithinWindow()
    {
        var policy = CreatePolicy(suppressionDays: 7);
        var user = NewUser();
        var sentAt = new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);
        user.MarkWelcomeEmailSent(sentAt);

        var decision = policy.ShouldSend(user, sentAt.AddDays(3));

        decision.Should().Be(WelcomeEmailPolicy.Decision.SuppressedRecentSend);
    }

    [Fact]
    public void ShouldSend_SuppressionLapsed_BeyondWindow()
    {
        var policy = CreatePolicy(suppressionDays: 7);
        var user = NewUser();
        var sentAt = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        user.MarkWelcomeEmailSent(sentAt);

        var decision = policy.ShouldSend(user, sentAt.AddDays(8));

        decision.Should().Be(WelcomeEmailPolicy.Decision.SuppressionLapsed);
    }

    [Fact]
    public void ShouldSend_SuppressionLapsed_ExactlyAtBoundary()
    {
        // Wave 11.4 behavior: at exactly SuppressionWindow elapsed, the email
        // re-fires (strict less-than in ShouldSend). This contract is preserved
        // verbatim — the instance-based refactor must not regress it.
        var policy = CreatePolicy(suppressionDays: 7);
        var user = NewUser();
        var sentAt = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        user.MarkWelcomeEmailSent(sentAt);

        var decision = policy.ShouldSend(user, sentAt.AddDays(7));

        decision.Should().Be(WelcomeEmailPolicy.Decision.SuppressionLapsed);
    }

    [Fact]
    public void ShouldSend_HonorsCustomSuppressionWindow()
    {
        // 14-day window: a re-send at day 10 is suppressed.
        var policy = CreatePolicy(suppressionDays: 14);
        var user = NewUser();
        var sentAt = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        user.MarkWelcomeEmailSent(sentAt);

        policy.ShouldSend(user, sentAt.AddDays(10))
            .Should().Be(WelcomeEmailPolicy.Decision.SuppressedRecentSend);
        policy.ShouldSend(user, sentAt.AddDays(15))
            .Should().Be(WelcomeEmailPolicy.Decision.SuppressionLapsed);
    }

    [Fact]
    public void ShouldSendBool_TrueUnlessSuppressedRecentSend()
    {
        var policy = CreatePolicy();
        var fresh = NewUser();
        var sent = NewUser();
        sent.MarkWelcomeEmailSent(DateTimeOffset.UtcNow.AddDays(-1));

        policy.ShouldSendBool(fresh, DateTimeOffset.UtcNow).Should().BeTrue();
        policy.ShouldSendBool(sent, DateTimeOffset.UtcNow).Should().BeFalse();
    }
}
