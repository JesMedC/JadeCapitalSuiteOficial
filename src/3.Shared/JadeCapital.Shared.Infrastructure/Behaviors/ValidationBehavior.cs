using FluentValidation;
using JadeCapital.Shared.Kernel.Exceptions;
using MediatR;

namespace JadeCapital.Shared.Infrastructure.Behaviors;

/// <summary>
/// Pipeline behavior de MediatR que ejecuta todos los <see cref="IValidator{TRequest}"/>
/// registrados en el scope antes de invocar el handler.
/// Si hay failures, lanza <see cref="JadeCapital.Shared.Kernel.Exceptions.ValidationException"/>
/// para que el ExceptionHandler del Host lo mapee a un 400 ProblemDetails.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, ct)));

        var failures = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
            throw new JadeCapital.Shared.Kernel.Exceptions.ValidationException(failures);

        return await next();
    }
}