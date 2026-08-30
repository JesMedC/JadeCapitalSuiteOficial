using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Trading.Application._Common;

public static class QuotesErrors
{
    public static readonly Error NotFound =
        Error.NotFound("quote", "Quote not found for symbol.");
}
