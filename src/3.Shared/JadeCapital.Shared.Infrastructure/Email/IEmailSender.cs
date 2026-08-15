namespace JadeCapital.Shared.Infrastructure.Email;

/// <summary>
/// Outbound-only abstraction for sending account-recovery emails. The
/// implementation is selected via DI per environment:
///   - <c>MailKitSmtpEmailSender</c> for production SMTP.
///   - <c>MailpitSmtpEmailSender</c> for local development (defaults to localhost:1025).
///   - <c>InMemoryCapturingEmailSender</c> for integration tests.
///
/// Slice 0c contract: implementations MUST never log the message body or the
/// temporary password. The <see cref="RecoveryEmailMessage.TemporaryPassword"/>
/// is consumed inside the SMTP layer and erased immediately; only correlation,
/// outcome, attempt, and latency are logged.
/// </summary>
public interface IEmailSender
{
    Task SendRecoveryEmailAsync(RecoveryEmailMessage message, CancellationToken ct = default);
}

/// <summary>
/// Value object that carries everything an email transport needs to compose
/// a recovery email. No plaintext credential ever touches a log line.
/// </summary>
public sealed record RecoveryEmailMessage(
    string To,
    string DisplayName,
    string TemporaryPassword,
    DateTimeOffset ExpiresAt);