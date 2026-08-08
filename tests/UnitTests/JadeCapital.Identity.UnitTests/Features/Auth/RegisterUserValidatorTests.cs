namespace JadeCapital.Identity.UnitTests.Features.Auth;

public class RegisterUserValidatorTests
{
    private readonly RegisterUserValidator _sut = new();

    [Fact]
    public void Valid_Command_Passes()
    {
        var cmd = new RegisterUserCommand("user" + "@" + "test.com", "Valid Name", "Passw0rd!Str");
        _sut.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("@no-local.com")]
    public void Invalid_Email_Fails(string email)
    {
        var cmd = new RegisterUserCommand(email, "Valid Name", "Passw0rd!Str");
        _sut.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    public void Invalid_DisplayName_Fails(string name)
    {
        var cmd = new RegisterUserCommand("user" + "@" + "test.com", name, "Passw0rd!Str");
        _sut.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("short")]               // < 10
    [InlineData("nodigitpassword!")]    // sin digitos
    public void Invalid_Password_Fails(string password)
    {
        var cmd = new RegisterUserCommand("user" + "@" + "test.com", "Valid Name", password);
        _sut.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void TooLongPassword_Fails()
    {
        var cmd = new RegisterUserCommand("user" + "@" + "test.com", "Valid Name", new string('a', 129));
        _sut.Validate(cmd).IsValid.Should().BeFalse();
    }
}