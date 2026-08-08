using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Shared.Kernel.Exceptions;

/// <summary>
/// Excepcion de dominio. Se lanza SOLO cuando un Result.Failure cruza una frontera
/// que no es la aplicacion (controllers, middleware, jobs). Lleva el Error semantico
/// para que el ExceptionHandlingMiddleware lo mapee a HTTP.
/// </summary>
public class DomainException : Exception
{
    public Error Error { get; }

    public DomainException(Error error) : base(error.Message)
    {
        Error = error;
    }
}

public sealed class NotFoundDomainException : DomainException
{
    public NotFoundDomainException(Error error) : base(error) { }
}

public sealed class ConflictDomainException : DomainException
{
    public ConflictDomainException(Error error) : base(error) { }
}

public sealed class UnauthorizedDomainException : DomainException
{
    public UnauthorizedDomainException(Error error) : base(error) { }
}

public sealed class ForbiddenDomainException : DomainException
{
    public ForbiddenDomainException(Error error) : base(error) { }
}