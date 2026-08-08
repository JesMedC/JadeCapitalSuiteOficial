using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Exceptions;

namespace JadeCapital.Shared.Kernel.Validation;

/// <summary>
/// Helper para Handlers: traduce un Result.Failure al throw tipado segun el prefijo del codigo.
/// Conveción: error code es "{categoria}.{detalle}" donde categoria ∈ {validation, notfound, conflict, ...}.
/// Evita if-else encadenados al final de cada handler.
/// </summary>
public static class DomainGuard
{
    public static void EnsureSuccess(Result result)
    {
        if (result.IsFailure)
        {
            throw MapToException(result.Error);
        }
    }

    public static void EnsureSuccess<T>(Result<T> result)
    {
        if (result.IsFailure)
        {
            throw MapToException(result.Error);
        }
    }

    private static Exception MapToException(Error error)
    {
        if (error.Code.StartsWith(Error.Prefixes.Validation, StringComparison.OrdinalIgnoreCase))
            return new ValidationException(
                new[] { new FluentValidation.Results.ValidationFailure(error.Code, error.Message) });

        if (error.Code.StartsWith(Error.Prefixes.NotFound, StringComparison.OrdinalIgnoreCase))
            return new NotFoundDomainException(error);

        if (error.Code.StartsWith(Error.Prefixes.Conflict, StringComparison.OrdinalIgnoreCase))
            return new ConflictDomainException(error);

        if (error.Code.StartsWith(Error.Prefixes.Unauthorized, StringComparison.OrdinalIgnoreCase))
            return new UnauthorizedDomainException(error);

        if (error.Code.StartsWith(Error.Prefixes.Forbidden, StringComparison.OrdinalIgnoreCase))
            return new ForbiddenDomainException(error);

        return new DomainException(error);
    }
}