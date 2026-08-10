using CurrencyValue = JadeCapital.Shared.Kernel.Money.Currency;

namespace JadeCapital.Shared.Kernel.UnitTests.Money;

public class CurrencyTests
{
    [Fact]
    public void Create_WithValidCode_Succeeds()
    {
        var r = CurrencyValue.Create("USD");

        r.IsSuccess.Should().BeTrue();
        r.Value.Code.Should().Be("USD");
    }

    [Fact]
    public void Create_LowercaseCode_NormalizesToUppercase()
    {
        var r = CurrencyValue.Create("usd");

        r.IsSuccess.Should().BeTrue();
        r.Value.Code.Should().Be("USD");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyOrNullCode_Fails(string? code)
    {
        var r = CurrencyValue.Create(code);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.currency.code_required");
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("U")]
    public void Create_WithInvalidLength_Fails(string code)
    {
        var r = CurrencyValue.Create(code);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.currency.code_invalid_length");
    }

    [Theory]
    [InlineData("US1")]
    [InlineData("US_")]
    [InlineData("US.")]
    [InlineData("U$D")]
    public void Create_WithNonLetterChars_Fails(string code)
    {
        var r = CurrencyValue.Create(code);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.currency.code_invalid_format");
    }

    [Fact]
    public void Create_WithUnsupportedCode_Fails()
    {
        var r = CurrencyValue.Create("ABC");

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.currency.code_unsupported");
    }

    [Fact]
    public void Equality_SameCode_AreEqual()
    {
        var a = CurrencyValue.Create("EUR").Value;
        var b = CurrencyValue.Create("eur").Value;

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentCode_NotEqual()
    {
        var usd = CurrencyValue.Create("USD").Value;
        var eur = CurrencyValue.Create("EUR").Value;

        usd.Should().NotBe(eur);
    }

    [Fact]
    public void StaticHelpers_ReturnExpectedCodes()
    {
        CurrencyValue.Usd.Code.Should().Be("USD");
        CurrencyValue.Eur.Code.Should().Be("EUR");
        CurrencyValue.Gbp.Code.Should().Be("GBP");
        CurrencyValue.Jpy.Code.Should().Be("JPY");
        CurrencyValue.Btc.Code.Should().Be("BTC");
        CurrencyValue.Eth.Code.Should().Be("ETH");
        CurrencyValue.Xau.Code.Should().Be("XAU");
    }

    [Fact]
    public void ToString_ReturnsCode()
    {
        var c = CurrencyValue.Create("GBP").Value;

        c.ToString().Should().Be("GBP");
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("GBP")]
    [InlineData("JPY")]
    [InlineData("CHF")]
    [InlineData("AUD")]
    [InlineData("CAD")]
    [InlineData("NZD")]
    [InlineData("XAU")]
    [InlineData("XAG")]
    [InlineData("BTC")]
    [InlineData("ETH")]
    public void Create_AcceptsAllListedSupportedCodes(string code)
    {
        var r = CurrencyValue.Create(code);

        r.IsSuccess.Should().BeTrue();
        r.Value.Code.Should().Be(code);
    }
}
