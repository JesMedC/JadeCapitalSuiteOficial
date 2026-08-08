namespace JadeCapital.Identity.Application.Abstractions;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = default!;
    public string Audience { get; set; } = default!;
    public string AccessTokenSecret { get; set; } = default!;
    public string RefreshTokenSecret { get; set; } = default!;
    public int AccessTokenTtlMinutes { get; set; } = 15;
    public int RefreshTokenTtlDays { get; set; } = 14;
    public int CleanupIntervalHours { get; set; } = 6;
}