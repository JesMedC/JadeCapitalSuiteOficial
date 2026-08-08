namespace JadeCapital.Identity.UnitTests.Users;

public class UserStateTransitionsTests
{
    private static User CreateActiveUser()
        => User.Register(Guid.NewGuid(), "user" + "@" + "test.com", "Test", "h", UserRole.Trader).Value;

    [Fact]
    public void Register_CreatesUserAlreadyActive()
    {
        var u = User.Register(Guid.NewGuid(), "user" + "@" + "test.com", "TestUser", "h", UserRole.Trader).Value;

        u.Status.Should().Be(UserStatus.Active);
        u.EmailConfirmedAt.Should().NotBeNull();
    }

    [Fact]
    public void ChangePassword_UpdatesHashAndRaisesEvent()
    {
        var u = CreateActiveUser();

        var r = u.ChangePassword("new-hash");

        r.IsSuccess.Should().BeTrue();
        u.PasswordHash.Should().Be("new-hash");
        u.DomainEvents.Should().Contain(e => e is UserPasswordChangedDomainEvent);
    }

    [Fact]
    public void ChangePassword_EmptyHash_Fails()
    {
        var u = CreateActiveUser();

        var r = u.ChangePassword("");

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.user.password_hash_required");
    }

    [Fact]
    public void ChangeDisplayName_UpdatesAndTrims()
    {
        var u = CreateActiveUser();

        var r = u.ChangeDisplayName("  NewName  ");

        r.IsSuccess.Should().BeTrue();
        u.DisplayName.Should().Be("NewName");
    }

    [Fact]
    public void ChangeDisplayName_TooShort_Fails()
    {
        var u = CreateActiveUser();

        var r = u.ChangeDisplayName("a");

        r.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void ChangeRole_RaisesDomainEvent()
    {
        var u = CreateActiveUser();

        var r = u.ChangeRole(UserRole.Admin);

        r.IsSuccess.Should().BeTrue();
        u.Role.Should().Be(UserRole.Admin);
        u.DomainEvents.Should().Contain(e => e is UserRoleChangedDomainEvent);
    }

    [Fact]
    public void Suspend_FromActive_SucceedsWithReason()
    {
        var u = CreateActiveUser();

        var r = u.Suspend("Fraude");

        r.IsSuccess.Should().BeTrue();
        u.Status.Should().Be(UserStatus.Suspended);
        u.DomainEvents.Should().Contain(e => e is UserSuspendedDomainEvent);
    }

    [Fact]
    public void Suspend_WithoutReason_Fails()
    {
        var u = CreateActiveUser();

        var r = u.Suspend("");

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.user.suspension_reason_required");
    }

    [Fact]
    public void Cancel_FromActive_SucceedsAndBlocksLogin()
    {
        var u = CreateActiveUser();

        var r = u.Cancel();

        r.IsSuccess.Should().BeTrue();
        u.Status.Should().Be(UserStatus.Cancelled);
        u.CanAuthenticate().Should().BeFalse();
        u.DomainEvents.Should().Contain(e => e is UserCancelledDomainEvent);
    }

    [Fact]
    public void Cancel_AlreadyCancelled_Fails()
    {
        var u = CreateActiveUser();
        u.Cancel();

        var r = u.Cancel();

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("conflict.user.already_cancelled");
    }

    [Fact]
    public void Suspend_Cancelled_Fails()
    {
        var u = CreateActiveUser();
        u.Cancel();

        var r = u.Suspend("x");

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("conflict.user.cannot_suspend_cancelled");
    }

    [Fact]
    public void Reactivate_FromSuspended_Restores()
    {
        var u = CreateActiveUser();
        u.Suspend("test");
        u.ClearDomainEvents();

        var r = u.Reactivate();

        r.IsSuccess.Should().BeTrue();
        u.Status.Should().Be(UserStatus.Active);
        u.DomainEvents.Should().Contain(e => e is UserReactivatedDomainEvent);
    }

    [Fact]
    public void Reactivate_FromCancelled_Fails()
    {
        var u = CreateActiveUser();
        u.Cancel();

        var r = u.Reactivate();

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("conflict.user.cannot_reactivate_cancelled");
    }
}