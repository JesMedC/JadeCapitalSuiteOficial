namespace JadeCapital.Shared.Kernel.Results;

/// <summary>
/// Error de dominio. Inmutable. Inmutable por código y mensaje.
/// Usar SIEMPRE constantes de Error.None para señalar ausencia de error.
/// Usar factories estaticas por categoria (Validation, NotFound, Conflict, etc.).
/// </summary>
public readonly record struct Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public bool IsNone => Code == string.Empty;

    /// <summary>Prefijos de categoria para que DomainGuard pueda enrutar el throw.</summary>
    public static class Prefixes
    {
        public const string Validation = "validation.";
        public const string NotFound = "notfound.";
        public const string Conflict = "conflict.";
        public const string Unauthorized = "unauthorized.";
        public const string Forbidden = "forbidden.";
        public const string Failure = "failure.";
        public const string Infrastructure = "infrastructure.";
    }

    public static Error Validation(string code, string message)
        => new(Prefixes.Validation + code, message);

    public static Error NotFound(string code, string message)
        => new(Prefixes.NotFound + code, message);

    public static Error Conflict(string code, string message)
        => new(Prefixes.Conflict + code, message);

    public static Error Unauthorized(string code, string message)
        => new(Prefixes.Unauthorized + code, message);

    public static Error Forbidden(string code, string message)
        => new(Prefixes.Forbidden + code, message);

    public static Error Failure(string code, string message)
        => new(Prefixes.Failure + code, message);

    public static Error Infrastructure(string code, string message)
        => new(Prefixes.Infrastructure + code, message);
}