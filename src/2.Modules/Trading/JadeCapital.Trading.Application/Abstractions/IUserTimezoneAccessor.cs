namespace JadeCapital.Trading.Application.Abstractions;

/// <summary>
/// Resuelve el timezone IANA del usuario actual a partir del request HTTP.
/// Implementacion tipica (<c>HttpHeaderTimezoneAccessor</c>) lee el header
/// <c>X-User-Timezone</c> y cae a <c>"UTC"</c> cuando esta ausente o tiene
/// un formato invalido.
///
/// Vive en Application/Abstractions (no en Api) porque el contrato es
/// consumido por handlers que necesitan resolver el timezone sin
/// acoplarse a <c>IHttpContextAccessor</c>. La implementacion vive en
/// Api/Timezone/ (depende de ASP.NET Core) y se registra como singleton
/// desde el Host.
///
/// ## Por que singleton
///
/// El accessor solo lee el header del request actual — no mantiene
/// estado entre requests. Un singleton es seguro porque cada invocacion
/// resuelve contra el <see cref="Microsoft.AspNetCore.Http.HttpContext"/>
/// del request activo (no un campo compartido). Ademas, registrar como
/// singleton evita un allocation por request para un helper trivial.
/// </summary>
public interface IUserTimezoneAccessor
{
    /// <summary>
    /// Devuelve el nombre IANA del timezone del usuario actual, o
    /// <c>"UTC"</c> cuando no se puede resolver (header ausente,
    /// formato invalido, fuera de un request HTTP scope).
    /// </summary>
    string GetTimezoneOrUtc();
}
