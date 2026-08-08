namespace JadeCapital.Identity.UnitTests.Users;

public class UserLoginTrackingTests
{
    private static User CreateActiveUser()
        => User.Register(Guid.NewGuid(), "user" + "@" + "test.com", "TestUser", "h", UserRole.Trader).Value;

    [Fact]
    public void RecordSuccessfulLogin_ResetsFailedCountAndUpdatesLastLogin()
    {
        var u = CreateActiveUser();

        var r = u.RecordSuccessfulLogin();

        r.IsSuccess.Should().BeTrue();
        u.FailedLoginCount.Should().Be(0);
        u.LastLoginAt.Should().NotBeNull();
    }

    [Fact]
    public void RecordFailedLogin_IncrementsCounter()
    {
        var u = CreateActiveUser();

        for (var i = 0; i < 3; i++) u.RecordFailedLogin();

        u.FailedLoginCount.Should().Be(3);
    }

    [Fact]
    public void RecordFailedLogin_AfterMaxAttempts_LocksAccount()
    {
        var u = CreateActiveUser();

        for (var i = 0; i < User.MaxFailedLoginAttempts; i++) u.RecordFailedLogin();

        u.Status.Should().Be(UserStatus.LockedOut);
        u.LockedUntil.Should().NotBeNull();
        u.LockedUntil.Should().BeAfter(DateTimeOffset.UtcNow);
        u.DomainEvents.Should().Contain(e => e is UserLockedOutDomainEvent);
    }

    [Fact]
    public void CanAuthenticate_ActiveUser_ReturnsTrue()
    {
        var u = CreateActiveUser();

        u.CanAuthenticate().Should().BeTrue();
    }

    [Fact]
    public void CanAuthenticate_CancelledUser_ReturnsFalse()
    {
        var u = CreateActiveUser();
        u.Cancel();

        u.CanAuthenticate().Should().BeFalse();
    }

    [Fact]
    public void CanAuthenticate_ActiveUserAfterRegister_ReturnsTrue()
    {
        var u = User.Register(Guid.NewGuid(), "user" + "@" + "test.com", "TestUser", "h", UserRole.Trader).Value;

        u.Status.Should().Be(UserStatus.Active);
        u.CanAuthenticate().Should().BeTrue();
    }

    [Fact]
    public void RecordSuccessfulLogin_OnLockedOut_Fails()
    {
        var u = CreateActiveUser();
        for (var i = 0; i < User.MaxFailedLoginAttempts; i++) u.RecordFailedLogin();

        var r = u.RecordSuccessfulLogin();

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("forbidden.user.locked_out_cannot_login");
    }

    [Fact]
    public void RecordSuccessfulLogin_OnSuspended_Fails()
    {
        var u = CreateActiveUser();
        u.Suspend("x");

        var r = u.RecordSuccessfulLogin();

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("forbidden.user.suspended_cannot_login");
    }
}