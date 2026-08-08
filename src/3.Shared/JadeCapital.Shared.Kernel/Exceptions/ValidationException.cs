using FluentValidation.Results;

namespace JadeCapital.Shared.Kernel.Exceptions;

/// <summary>
/// Excepcion para errores de validacion. Contiene TODOS los failures de FluentValidation
/// para que el cliente reciba un detalle completo en una sola respuesta.
/// </summary>
public sealed class ValidationException : Exception
{
    public IReadOnlyList<ValidationFailure> Failures { get; }

    public ValidationException(IEnumerable<ValidationFailure> failures)
        : base("One or more validation errors occurred.")
    {
        Failures = failures.ToList();
    }
}