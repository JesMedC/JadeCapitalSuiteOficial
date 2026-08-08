namespace JadeCapital.Identity.UnitTests.Users;

public class UserRegistrationTests
{
    [Fact]
    public void Register_WithValidData_CreatesActiveUser()
    {
        var id = Guid.NewGuid();
        var email = "user" + "@" + "test.com";
        var result = User.Register(id, email, "Jesus M.", "hashed-pwd", UserRole.Trader);

        result.IsSuccess.Should().BeTrue();
        var u = result.Value;
        u.Id.Should().Be(id);
        u.Email.Should().Be(email);
        u.DisplayName.Should().Be("Jesus M.");
        u.Role.Should().Be(UserRole.Trader);
        u.Status.Should().Be(UserStatus.Active);
        u.EmailConfirmedAt.Should().NotBeNull();
        u.FailedLoginCount.Should().Be(0);
        u.LockedUntil.Should().BeNull();
        u.DomainEvents.Should().ContainSingle(e => e is UserRegisteredDomainEvent);
    }

    [Fact]
    public void Register_UppercaseEmail_NormalizesToLowercase()
    {
        var email = "USER" + "@" + "TEST.COM";
        var result = User.Register(Guid.NewGuid(), email, "Display", "h", UserRole.Trader);

        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Should().Be(("USER" + "@" + "TEST.COM").ToLowerInvariant());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("@no-local.com")]
    public void Register_WithInvalidEmail_FailsValidation(string email)
    {
        var result = User.Register(Guid.NewGuid(), email, "Valid Name", "h", UserRole.Trader);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.user.email_invalid");
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("   ")]
    public void Register_WithTooShortDisplayName_Fails(string name)
    {
        var result = User.Register(Guid.NewGuid(), "user" + "@" + "test.com", name, "h", UserRole.Trader);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.user.display_name_invalid");
    }

    [Fact]
    public void Register_WithEmptyPasswordHash_Fails()
    {
        var result = User.Register(Guid.NewGuid(), "user" + "@" + "test.com", "Name", "", UserRole.Trader);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.user.password_hash_required");
    }

    [Fact]
    public void Register_WithEmptyId_Fails()
    {
        var result = User.Register(Guid.Empty, "user" + "@" + "test.com", "Name", "h", UserRole.Trader);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.user.id_required");
    }
}