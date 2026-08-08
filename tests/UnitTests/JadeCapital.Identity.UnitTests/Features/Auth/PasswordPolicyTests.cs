namespace JadeCapital.Identity.UnitTests.Features.Auth;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("")]                     // empty
    [InlineData("aaaaaaaaaa")]           // no digits
    [InlineData("1234567890")]           // no letters
    public void MeetsComplexity_RejectsWeakPasswords(string password)
    {
        // MeetsComplexity valida SOLO complejidad (letras+digitos+unicos).
        // La longitud la valida FluentValidation aparte.
        PasswordPolicy.MeetsComplexity(password).Should().BeFalse();
    }

    [Theory]
    [InlineData("Passw0rd!")]            // letter+digit+special
    [InlineData("Str0ngPass")]
    [InlineData("C0mpl3j0T0!")]
    public void MeetsComplexity_AcceptsStrongPasswords(string password)
    {
        PasswordPolicy.MeetsComplexity(password).Should().BeTrue();
    }

    [Fact]
    public void MeetsComplexity_TooFewUniqueChars_Fails()
    {
        // Solo letras repetidas + 1 digito: {a, b, 1} = 3 unicos < MinUniqueChars.
        // Ademas, NO es fail por falta de digitos/letras (si tiene ambos).
        // Probamos que un patron debil con pocos unicos falle.
        PasswordPolicy.MeetsComplexity("aaaaaaaa1").Should().BeFalse();
        // Solo 2 unicos: a y b. Sin digitos ademas => falla por no-digit.
        PasswordPolicy.MeetsComplexity("aabb").Should().BeFalse();
    }
}