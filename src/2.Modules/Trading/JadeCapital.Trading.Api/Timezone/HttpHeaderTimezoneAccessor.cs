using System.Text.RegularExpressions;
using JadeCapital.Trading.Application.Abstractions;

namespace JadeCapital.Trading.Api.Timezone;

/// <summary>
/// Implementacion por defecto de <see cref="IUserTimezoneAccessor"/>
/// (slice 2a.1).
///
/// Resuelve el timezone IANA del usuario actual a partir del header
/// HTTP <c>X-User-Timezone</c>. Si el header esta ausente, vacio o
/// tiene un formato invalido, devuelve <c>"UTC"</c> como fallback
/// seguro.
///
/// ## Validacion
///
/// No hacemos un TZif lookup completo (eso es overkill para Wave 2).
/// Validamos solo el shape basico de un nombre IANA:
/// <c>Region/City</c> o <c>Region/Subregion/City</c>. La regex es:
/// <c>^[A-Za-z]+/[A-Za-z_]+(/[A-Za-z_]+)?$</c>
///
/// Esto cubre el 99% de los timezones reales (e.g.
/// <c>America/Argentina/Buenos_Aires</c>, <c>Europe/Madrid</c>,
/// <c>UTC</c>) sin aceptar strings malformados que harian fallar
/// <see cref="TimeZoneInfo.FindSystemTimeZoneById"/> mas adelante.
/// Si el FE envia un timezone que pasa nuestra regex pero el OS no
/// reconoce, <see cref="JadeCapital.Shared.Kernel.Time.LocalDate.From"/>
/// cae a UTC como red de seguridad.
///
/// ## Por que singleton
///
/// El accessor no mantiene estado — cada invocacion resuelve contra
/// el <c>HttpContext</c> del request activo (vía
/// <see cref="IHttpContextAccessor"/>). Registrar como singleton
/// evita un allocation por request para un helper trivial.
/// </summary>
public sealed class HttpHeaderTimezoneAccessor : IUserTimezoneAccessor
{
    private const string HeaderName = "X-User-Timezone";
    private const string FallbackTimezone = "UTC";

    /// <summary>
    /// Regex basica para validar el shape de un nombre IANA:
    /// <c>Region/City</c> o <c>Region/Subregion/City</c>. Permite letras
    /// y guion bajo (este ultimo para zonas como <c>America/Argentina/ComodRivadavia</c>
    /// que tienen guion bajo historico).
    /// </summary>
    private static readonly Regex IanaShapeRegex = new(
        @"^[A-Za-z]+/[A-Za-z_]+(/[A-Za-z_]+)?$",
        RegexOptions.Compiled);

    private readonly IHttpContextAccessor _http;

    public HttpHeaderTimezoneAccessor(IHttpContextAccessor http)
    {
        _http = http;
    }

    public string GetTimezoneOrUtc()
    {
        var ctx = _http.HttpContext;
        if (ctx is null)
            return FallbackTimezone;

        // Headers son case-insensitive en HTTP; IHeaderDictionary lo
        // respeta. Devolvemos el primer valor si el FE envia multiples.
        if (!ctx.Request.Headers.TryGetValue(HeaderName, out var values))
            return FallbackTimezone;

        var raw = values.ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return FallbackTimezone;

        var trimmed = raw.Trim();
        if (!IanaShapeRegex.IsMatch(trimmed))
            return FallbackTimezone;

        return trimmed;
    }
}
