namespace JadeCapital.Identity.UnitTests.Authentication;

public class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void Issue_WithValidData_CreatesActiveToken()
    {
        var id = Guid.NewGuid();
        var hash = "abc123hash";

        var r = RefreshToken.Issue(id, Guid.NewGuid(), hash, Now, Now.AddDays(14), "1.1.1.1", "Chrome");

        r.IsSuccess.Should().BeTrue();
        var t = r.Value;
        t.IsActive(Now).Should().BeTrue();
        t.IsExpired(Now).Should().BeFalse();
        t.RevokedAt.Should().BeNull();
    }

    [Fact]
    public void Issue_WithExpiryBeforeIssued_Fails()
    {
        var r = RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "h", Now, Now.AddSeconds(-1));

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("unauthorized.refresh_token.expired");
    }

    [Fact]
    public void Issue_WithEmptyHash_Fails()
    {
        var r = RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "", Now, Now.AddDays(1));

        r.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Issue_WithEmptyUserId_Fails()
    {
        var r = RefreshToken.Issue(Guid.NewGuid(), Guid.Empty, "h", Now, Now.AddDays(1));

        r.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Revoke_MarksAsRevoked()
    {
        var t = RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "h", Now, Now.AddDays(1)).Value;

        var r = t.Revoke(Now, Guid.NewGuid());

        r.IsSuccess.Should().BeTrue();
        t.RevokedAt.Should().Be(Now);
        t.IsActive(Now).Should().BeFalse();
    }

    [Fact]
    public void Revoke_AlreadyRevoked_Fails()
    {
        var t = RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "h", Now, Now.AddDays(1)).Value;
        t.Revoke(Now, Guid.NewGuid());

        var r = t.Revoke(Now, Guid.NewGuid());

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("conflict.refresh_token.already_revoked");
    }

    [Fact]
    public void IsActive_ExpiredToken_ReturnsFalse()
    {
        var t = RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "h", Now, Now.AddMinutes(1)).Value;

        t.IsActive(Now.AddMinutes(2)).Should().BeFalse();
        t.IsExpired(Now.AddMinutes(2)).Should().BeTrue();
    }

    [Fact]
    public void IsActive_FreshToken_ReturnsTrue()
    {
        var t = RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "h", Now, Now.AddDays(14)).Value;

        t.IsActive(Now.AddDays(1)).Should().BeTrue();
        t.IsExpired(Now.AddDays(1)).Should().BeFalse();
    }
}