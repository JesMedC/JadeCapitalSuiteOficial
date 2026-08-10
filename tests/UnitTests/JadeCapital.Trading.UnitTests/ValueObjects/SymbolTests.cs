namespace JadeCapital.Trading.UnitTests.ValueObjects;

public class SymbolTests
{
    [Fact]
    public void Create_WithValidForexSymbol_Succeeds()
    {
        var r = Symbol.Create("EUR/USD");

        r.IsSuccess.Should().BeTrue();
        r.Value.Value.Should().Be("EUR/USD");
    }

    [Fact]
    public void Create_LowercaseSymbol_NormalizesToUppercase()
    {
        var r = Symbol.Create("eur/usd");

        r.IsSuccess.Should().BeTrue();
        r.Value.Value.Should().Be("EUR/USD");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyOrNullValue_Fails(string? value)
    {
        var r = Symbol.Create(value);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.symbol.value_required");
    }

    [Theory]
    [InlineData("AB")]
    [InlineData("A")]
    public void Create_TooShort_Fails(string value)
    {
        var r = Symbol.Create(value);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.symbol.value_too_short");
    }

    [Fact]
    public void Create_TooLong_Fails()
    {
        var r = Symbol.Create("ABCDEFGHIJKLMNOPQRSTU"); // 21 chars

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.symbol.value_too_long");
    }

    [Theory]
    [InlineData("EUR USD")]   // space
    [InlineData("EUR-USD")]   // dash
    [InlineData("EUR.USD")]   // dot
    [InlineData("EUR@USD")]   // @
    public void Create_InvalidCharacters_Fails(string value)
    {
        var r = Symbol.Create(value);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.symbol.value_invalid_format");
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var a = Symbol.Create("BTC/USD").Value;
        var b = Symbol.Create("BTC/USD").Value;

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        var s = Symbol.Create("XAU/USD").Value;

        s.ToString().Should().Be("XAU/USD");
    }

    [Fact]
    public void DetectAssetClass_CommodityPrefix_ReturnsCommodity()
    {
        Symbol.Create("XAU/USD").Value.DetectAssetClass().Should().Be(AssetClass.Commodity);
        Symbol.Create("XAG/USD").Value.DetectAssetClass().Should().Be(AssetClass.Commodity);
    }

    [Fact]
    public void DetectAssetClass_CryptoPrefix_ReturnsCrypto()
    {
        Symbol.Create("BTC/USD").Value.DetectAssetClass().Should().Be(AssetClass.Crypto);
        Symbol.Create("ETH/USDT").Value.DetectAssetClass().Should().Be(AssetClass.Crypto);
    }

    [Fact]
    public void DetectAssetClass_ForexPair_ReturnsForex()
    {
        Symbol.Create("EUR/USD").Value.DetectAssetClass().Should().Be(AssetClass.Forex);
        Symbol.Create("GBP/JPY").Value.DetectAssetClass().Should().Be(AssetClass.Forex);
    }

    [Fact]
    public void DetectAssetClass_BinarySynthetic_ReturnsBinary()
    {
        Symbol.Create("BOOM500").Value.DetectAssetClass().Should().Be(AssetClass.Binary);
        Symbol.Create("CRASH1000").Value.DetectAssetClass().Should().Be(AssetClass.Binary);
        Symbol.Create("JD10").Value.DetectAssetClass().Should().Be(AssetClass.Binary);
    }

    [Fact]
    public void DetectAssetClass_UnknownNoSeparator_ReturnsOther()
    {
        Symbol.Create("ZZZZZ").Value.DetectAssetClass().Should().Be(AssetClass.Other);
    }

    [Fact]
    public void InferQuoteCurrencyCode_WithSlash_ReturnsRightPart()
    {
        Symbol.Create("EUR/USD").Value.InferQuoteCurrencyCode().Should().Be("USD");
        Symbol.Create("BTC/USDT").Value.InferQuoteCurrencyCode().Should().Be("USDT");
        Symbol.Create("XAU/USD").Value.InferQuoteCurrencyCode().Should().Be("USD");
    }

    [Fact]
    public void InferQuoteCurrencyCode_NoSlash_DefaultsToLast3Or4Chars()
    {
        Symbol.Create("BTCUSD").Value.InferQuoteCurrencyCode().Should().Be("USD");
        Symbol.Create("XAUUSD").Value.InferQuoteCurrencyCode().Should().Be("USD");
        Symbol.Create("BTCUSDT").Value.InferQuoteCurrencyCode().Should().Be("USDT");
    }
}
