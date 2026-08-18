using JadeCapital.Shared.Kernel.MultiTenancy;

namespace JadeCapital.Identity.Application.Abstractions;

/// <summary>
/// Servicio de tokens. Emite access tokens (JWT firmado HS256) y refresh tokens opacos.
/// La implementacion (JwtTokenService) vive en Infrastructure porque depende de
/// Microsoft.IdentityModel.Tokens.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Emite un access token firmado con la clave de acceso. TTL corto (15 min default).
    /// <para>
    /// <b>Wave 6, slice 6c.2</b>: when <paramref name="tenantId"/> is
    /// non-null the token carries a <c>tenant_id</c> claim that the
    /// <c>TenantContextMiddleware</c> reads. When null the claim is
    /// omitted entirely so the middleware gates the request with
    /// <c>auth.tenant_missing</c>.
    /// </para>
    /// </summary>
    (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(
        Guid userId,
        string email,
        string role,
        IEnumerable<string>? extraClaims = null,
        TenantId? tenantId = null);

    /// <summary>Genera un refresh token opaco (base64 url-safe de 48 bytes random). Solo lo ve el cliente.</summary>
    string CreateOpaqueRefreshToken();

    /// <summary>Hashea el token para guardarlo en BD (nunca en plano).</summary>
    string HashToken(string token);

    /// <summary>Parametros para validar JWT en middleware. Vive en Infrastructure porque requiere opciones.</summary>
    // Mantenido minimo aqui; la validacion real es responsabilidad de Microsoft.AspNetCore.Authentication.JwtBearer.
}