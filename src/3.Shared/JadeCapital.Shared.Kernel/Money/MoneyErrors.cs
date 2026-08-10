using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Shared.Kernel.Money;

/// <summary>
/// Errores semanticos de los Value Objects de Money/Currency.
/// Codigo = "{boundedContext}.{entidad}.{detalle}".
/// </summary>
public static class MoneyErrors
{
    public static class Currency
    {
        public static readonly Error CodeRequired =
            Error.Validation("currency.code_required", "Currency code is required.");

        public static readonly Error CodeInvalidLength =
            Error.Validation("currency.code_invalid_length", "Currency code must be exactly 3 letters.");

        public static readonly Error CodeInvalidFormat =
            Error.Validation("currency.code_invalid_format", "Currency code must contain only ASCII letters.");

        public static readonly Error CodeUnsupported =
            Error.Validation("currency.code_unsupported", "Currency code is not in the supported list.");
    }

    public static class Money
    {
        public static readonly Error AmountOutOfRange =
            Error.Validation("money.amount_out_of_range", "Amount is out of range for NUMERIC(24,8) storage.");

        public static readonly Error CurrencyMismatch =
            Error.Validation("money.currency_mismatch", "Currencies must match for this operation.");
    }
}
