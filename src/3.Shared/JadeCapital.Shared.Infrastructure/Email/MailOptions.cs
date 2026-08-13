namespace JadeCapital.Shared.Infrastructure.Email;

/// <summary>
/// Mail transport options bound from configuration key <c>Mail</c>. Required
/// for production (<see cref="MailKitSmtpEmailSender"/>); <see cref="Host"/>
/// defaults to <c>localhost</c> so the same class can target a local
/// Mailpit container with no extra wiring.
/// </summary>
public sealed class MailOptions
{
    public const string SectionName = "Mail";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = "no-reply@jadecapital.test";
    public bool UseStartTls { get; set; }

    /// <summary>Total time budget per attempt (initial + retries). Design budget: 13s.</summary>
    public int TimeoutMs { get; set; } = 4_000;
    public int MaxAttempts { get; set; } = 3;
}