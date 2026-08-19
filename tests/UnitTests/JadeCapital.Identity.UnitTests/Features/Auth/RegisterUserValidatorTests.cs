namespace JadeCapital.Identity.UnitTests.Features.Auth;

public class RegisterUserValidatorTests
{
    private readonly RegisterUserValidator _sut = new();

    private const string ToS = "v1.0";
    private const string Privacy = "v1.0";
    private const string Ip = "203.0.113.42";

    [Fact]
    public void Valid_Command_Passes()
    {
        var cmd = new RegisterUserCommand(
            "user" + "@" + "test.com",
            "Valid Name",
            "Passw0rd!Str",
            AcceptTerms: true,
            AcceptPrivacy: true,
            ConsentIp: Ip,
            AcceptedTermsVersion: ToS,
            AcceptedPrivacyVersion: Privacy);
        _sut.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("@no-local.com")]
    public void Invalid_Email_Fails(string email)
    {
        var cmd = new RegisterUserCommand(
            email, "Valid Name", "Passw0rd!Str",
            AcceptTerms: true, AcceptPrivacy: true, ConsentIp: Ip,
            AcceptedTermsVersion: ToS, AcceptedPrivacyVersion: Privacy);
        _sut.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    public void Invalid_DisplayName_Fails(string name)
    {
        var cmd = new RegisterUserCommand(
            "user" + "@" + "test.com", name, "Passw0rd!Str",
            AcceptTerms: true, AcceptPrivacy: true, ConsentIp: Ip,
            AcceptedTermsVersion: ToS, AcceptedPrivacyVersion: Privacy);
        _sut.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("short")]               // < 10
    [InlineData("nodigitpassword!")]    // sin digitos
    public void Invalid_Password_Fails(string password)
    {
        var cmd = new RegisterUserCommand(
            "user" + "@" + "test.com", "Valid Name", password,
            AcceptTerms: true, AcceptPrivacy: true, ConsentIp: Ip,
            AcceptedTermsVersion: ToS, AcceptedPrivacyVersion: Privacy);
        _sut.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void TooLongPassword_Fails()
    {
        var cmd = new RegisterUserCommand(
            "user" + "@" + "test.com", "Valid Name", new string('a', 129),
            AcceptTerms: true, AcceptPrivacy: true, ConsentIp: Ip,
            AcceptedTermsVersion: ToS, AcceptedPrivacyVersion: Privacy);
        _sut.Validate(cmd).IsValid.Should().BeFalse();
    }

    // ===== Wave 11 slice 11.4 — GDPR Art. 7 consent gate =====

    [Fact]
    public void TermsNotAccepted_Fails()
    {
        var cmd = new RegisterUserCommand(
            "user" + "@" + "test.com", "Valid Name", "Passw0rd!Str",
            AcceptTerms: false, AcceptPrivacy: true, ConsentIp: Ip,
            AcceptedTermsVersion: ToS, AcceptedPrivacyVersion: Privacy);
        var result = _sut.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorCode == "validation.auth.terms_required");
        result.Errors.Should().Contain(e => e.PropertyName == "AcceptTerms");
    }

    [Fact]
    public void PrivacyNotAccepted_Fails()
    {
        var cmd = new RegisterUserCommand(
            "user" + "@" + "test.com", "Valid Name", "Passw0rd!Str",
            AcceptTerms: true, AcceptPrivacy: false, ConsentIp: Ip,
            AcceptedTermsVersion: ToS, AcceptedPrivacyVersion: Privacy);
        var result = _sut.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorCode == "validation.auth.privacy_required");
        result.Errors.Should().Contain(e => e.PropertyName == "AcceptPrivacy");
    }

    [Fact]
    public void MissingConsentIp_Fails()
    {
        var cmd = new RegisterUserCommand(
            "user" + "@" + "test.com", "Valid Name", "Passw0rd!Str",
            AcceptTerms: true, AcceptPrivacy: true, ConsentIp: null,
            AcceptedTermsVersion: ToS, AcceptedPrivacyVersion: Privacy);
        var result = _sut.Validate(cmd);
        result.IsValid.Should().BeFalse();
    }
}
