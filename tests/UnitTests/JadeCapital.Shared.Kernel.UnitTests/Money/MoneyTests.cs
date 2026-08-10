using MoneyValue = JadeCapital.Shared.Kernel.Money.Money;
using CurrencyValue = JadeCapital.Shared.Kernel.Money.Currency;

namespace JadeCapital.Shared.Kernel.UnitTests.Money;

public class MoneyTests
{
    [Fact]
    public void Create_WithValidAmountAndCurrency_Succeeds()
    {
        var r = MoneyValue.Create(123.45m, CurrencyValue.Usd);

        r.IsSuccess.Should().BeTrue();
        r.Value.Amount.Should().Be(123.45m);
        r.Value.Currency.Should().Be(CurrencyValue.Usd);
    }

    [Fact]
    public void Create_WithNegativeAmount_Succeeds()
    {
        var r = MoneyValue.Create(-50m, CurrencyValue.Eur);

        r.IsSuccess.Should().BeTrue();
        r.Value.Amount.Should().Be(-50m);
    }

    [Fact]
    public void Create_WithZeroAmount_Succeeds()
    {
        var r = MoneyValue.Create(0m, CurrencyValue.Usd);

        r.IsSuccess.Should().BeTrue();
        r.Value.Amount.Should().Be(0m);
    }

    [Fact]
    public void Create_AmountAboveMax_Fails()
    {
        var r = MoneyValue.Create(MoneyValue.MaxAmount, CurrencyValue.Usd);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.money.amount_out_of_range");
    }

    [Fact]
    public void Create_AmountBelowNegativeMax_Fails()
    {
        var r = MoneyValue.Create(-MoneyValue.MaxAmount, CurrencyValue.Usd);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.money.amount_out_of_range");
    }

    [Fact]
    public void Create_JustBelowMax_Succeeds()
    {
        var r = MoneyValue.Create(MoneyValue.MaxAmount - 1m, CurrencyValue.Usd);

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Add_TwoSameCurrency_Succeeds()
    {
        var a = MoneyValue.Create(100m, CurrencyValue.Usd).Value;
        var b = MoneyValue.Create(50.25m, CurrencyValue.Usd).Value;

        var r = a + b;

        r.IsSuccess.Should().BeTrue();
        r.Value.Amount.Should().Be(150.25m);
        r.Value.Currency.Should().Be(CurrencyValue.Usd);
    }

    [Fact]
    public void Add_DifferentCurrency_Fails()
    {
        var usd = MoneyValue.Create(100m, CurrencyValue.Usd).Value;
        var eur = MoneyValue.Create(100m, CurrencyValue.Eur).Value;

        var r = usd + eur;

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.money.currency_mismatch");
    }

    [Fact]
    public void Subtract_SameCurrency_Succeeds()
    {
        var a = MoneyValue.Create(100m, CurrencyValue.Usd).Value;
        var b = MoneyValue.Create(30m, CurrencyValue.Usd).Value;

        var r = a - b;

        r.IsSuccess.Should().BeTrue();
        r.Value.Amount.Should().Be(70m);
    }

    [Fact]
    public void Subtract_DifferentCurrency_Fails()
    {
        var usd = MoneyValue.Create(100m, CurrencyValue.Usd).Value;
        var eur = MoneyValue.Create(100m, CurrencyValue.Eur).Value;

        var r = usd - eur;

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.money.currency_mismatch");
    }

    [Fact]
    public void Multiply_ByScalar_PreservesCurrency()
    {
        var price = MoneyValue.Create(2.50m, CurrencyValue.Usd).Value;

        var r = price * 4m;

        r.IsSuccess.Should().BeTrue();
        r.Value.Amount.Should().Be(10m);
        r.Value.Currency.Should().Be(CurrencyValue.Usd);
    }

    [Fact]
    public void Multiply_ByZero_ProducesZero()
    {
        var m = MoneyValue.Create(99m, CurrencyValue.Usd).Value;

        var r = m * 0m;

        r.IsSuccess.Should().BeTrue();
        r.Value.Amount.Should().Be(0m);
    }

    [Fact]
    public void Multiply_Overflows_Fails()
    {
        var big = MoneyValue.Create(MoneyValue.MaxAmount / 2m, CurrencyValue.Usd).Value;

        var r = big * 3m;

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.money.amount_out_of_range");
    }

    [Fact]
    public void Equality_SameAmountAndCurrency_AreEqual()
    {
        var a = MoneyValue.Create(10m, CurrencyValue.Usd).Value;
        var b = MoneyValue.Create(10m, CurrencyValue.Usd).Value;

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentAmount_NotEqual()
    {
        var a = MoneyValue.Create(10m, CurrencyValue.Usd).Value;
        var b = MoneyValue.Create(11m, CurrencyValue.Usd).Value;

        a.Should().NotBe(b);
    }

    [Fact]
    public void Equality_DifferentCurrency_NotEqual()
    {
        var a = MoneyValue.Create(10m, CurrencyValue.Usd).Value;
        var b = MoneyValue.Create(10m, CurrencyValue.Eur).Value;

        a.Should().NotBe(b);
    }

    [Fact]
    public void ToString_FormatsAmountAndCurrency()
    {
        var m = MoneyValue.Create(1.23456m, CurrencyValue.Usd).Value;

        m.ToString().Should().Be("1.23456 USD");
    }
}
