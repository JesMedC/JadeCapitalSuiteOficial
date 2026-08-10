using JadeCapital.Shared.Kernel.Money;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JadeCapital.Trading.Infrastructure.Persistence.Converters;

/// <summary>
/// ValueConverter Money -&gt; decimal para serializar Money.Amount en columnas
/// NUMERIC(24,8). NO se usa en la configuracion actual de Trade (se usa el
/// patron OwnsOne recomendado en el spec), pero queda como utilidad documentada
/// por si en el futuro se necesita mapear Money como columna escalar.
///
/// Para que funcione correctamente, la columna destino deberia ser CHAR(3)
/// para CurrencyCode + NUMERIC(24,8) para Amount. Como EF no soporta mapear
/// un objeto a dos columnas via HasConversion, este converter solo seria
/// util si se persiste Money como decimal puro (perdiendo Currency), lo cual
/// es INSEGURO. Por eso no se usa.
/// </summary>
public sealed class MoneyConverter : ValueConverter<Money, decimal>
{
    public MoneyConverter()
        : base(m => m.Amount, v => Money.FromTrusted(v, Currency.FromTrustedCode("USD")))
    {
        // NOTA: este converter es solo referencia. NO usar en configuracion.
        // El mapeo real es via OwnsOne en TradeConfiguration.
    }
}

/// <summary>
/// ValueComparer para detectar cambios en Money (que es inmutable). Compara por
/// Amount y CurrencyCode — suficiente porque Money es value object sin identidad.
/// </summary>
public sealed class MoneyComparer : ValueComparer<Money>
{
    public MoneyComparer()
        : base(
            (a, b) => a!.Amount == b!.Amount && a.CurrencyCode == b.CurrencyCode,
            v => HashCode.Combine(v.Amount, v.CurrencyCode),
            v => Money.FromTrusted(v.Amount, Currency.FromTrustedCode(v.CurrencyCode)))
    {
    }
}
