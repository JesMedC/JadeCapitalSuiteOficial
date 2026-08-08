namespace JadeCapital.Shared.Kernel.Results;

/// <summary>
/// Result<T> — Railway Oriented Programming para el dominio.
/// Cualquier operacion que pueda fallar por una razon de negocio ESPERADA
/// devuelve Result en lugar de lanzar excepcion.
/// Excepciones solo para casos verdaderamente excepcionales (infra caida, bug).
/// </summary>
public sealed class Result<T>
{
    private readonly T? _value;
    private readonly Error _error;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public T Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException(
                $"Cannot access Value when result is failure. Error: {_error.Code}");

    public Error Error => IsFailure
        ? _error
        : throw new InvalidOperationException(
            $"Cannot access Error when result is success.");

    private Result(T? value, bool isSuccess, Error error)
    {
        _value = value;
        IsSuccess = isSuccess;
        _error = error;
    }

    public static Result<T> Success(T value)
        => new(value, true, Error.None);

    public static Result<T> Failure(Error error)
        => new(default, false, error);
}

/// <summary>Variant sin payload. Para comandos cuyo unico output relevante es exito/fallo.</summary>
public sealed class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    private Result(bool isSuccess, Error error)
    {
        if (isSuccess && !error.IsNone)
            throw new InvalidOperationException("Successful result cannot carry an error.");
        if (!isSuccess && error.IsNone)
            throw new InvalidOperationException("Failed result must carry an error.");

        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);

    public static implicit operator Result(Error error) => Failure(error);

    /// <summary>Helper para construir un Result tipado cuando no se quiere escribir Result&lt;T&gt;.Failure.</summary>
    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);

    /// <summary>Helper para construir un Result tipado de éxito.</summary>
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
}