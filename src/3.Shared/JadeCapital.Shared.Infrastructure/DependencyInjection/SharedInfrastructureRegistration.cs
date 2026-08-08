using FluentValidation;
using JadeCapital.Shared.Infrastructure.Behaviors;
using JadeCapital.Shared.Kernel.Time;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Shared.Infrastructure.DependencyInjection;

public static class SharedInfrastructureRegistration
{
    /// <summary>
    /// Registra los servicios transversales de Shared.Infrastructure:
    /// - <see cref="IClock"/> como singleton (SystemClock).
    /// - <see cref="ValidationBehavior{TRequest,TResponse}"/> como pipeline behavior de MediatR.
    ///
    /// Para registrar los <see cref="IValidator{T}"/> del assembly caller, llamar
    /// <c>services.AddValidatorsFromAssembly(typeof(MarkerType).Assembly)</c> desde el modulo.
    /// </summary>
    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        return services;
    }

    /// <summary>
    /// Helper para registrar todos los validators de FluentValidation de un assembly.
    /// Wrapper de la extension de FluentValidation.DependencyInjectionExtensions
    /// para mantener una unica superficie de extension del lado de Shared.Infrastructure.
    /// </summary>
    public static IServiceCollection AddAssemblyValidators(this IServiceCollection services, System.Reflection.Assembly assembly)
    {
        return services.AddValidatorsFromAssembly(assembly);
    }
}