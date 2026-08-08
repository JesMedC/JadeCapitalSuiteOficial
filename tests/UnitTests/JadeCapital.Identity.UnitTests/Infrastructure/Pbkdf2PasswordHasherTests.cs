namespace JadeCapital.Identity.UnitTests.Infrastructure;

public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _sut = new(iterations: 100_000);

    [Fact]
    public void Hash_ProducesDifferentHashForSamePassword()
    {
        var h1 = _sut.Hash("Passw0rd!Str");
        var h2 = _sut.Hash("Passw0rd!Str");
        h1.Should().NotBe(h2); // sal distinta
    }

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var hash = _sut.Hash("Passw0rd!Str");
        _sut.Verify("Passw0rd!Str", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hash = _sut.Hash("Passw0rd!Str");
        _sut.Verify("wrong", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_EmptyInputs_ReturnFalse()
    {
        var hash = _sut.Hash("Passw0rd!Str");
        _sut.Verify("", hash).Should().BeFalse();
        _sut.Verify("anything", "").Should().BeFalse();
    }

    [Fact]
    public void Verify_MalformedHash_ReturnsFalse()
    {
        _sut.Verify("anything", "not-a-valid-hash").Should().BeFalse();
    }

    [Fact]
    public void Hash_NullOrEmptyPassword_Throws()
    {
        Action act1 = () => _sut.Hash("");
        Action act2 = () => _sut.Hash(null!);
        act1.Should().Throw<ArgumentException>();
        act2.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_LowIterations_Throws()
    {
        Action act = () => new Pbkdf2PasswordHasher(iterations: 10_000);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}