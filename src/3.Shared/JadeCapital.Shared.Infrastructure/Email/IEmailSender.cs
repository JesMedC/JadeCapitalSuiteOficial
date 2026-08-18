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
///
/// Slice 6c.3 extends the contract with <see cref="SendTenantInviteAsync"/> —
/// the Wave 6 multi-tenant admin endpoint invite flow. The actual mime
/// composition for invites is intentionally a stub for 6c.3 (the
/// <c>MailKitSmtpEmailSender</c> logs a warning + returns success); a future
/// slice wires the Spanish/Jade-branded invite template. Tests use
/// <see cref="IEmailSender"/> through NSubstitute so the stub is enough to
/// drive the <c>InviteTenantUserHandler</c> assertions.
/// </summary>
public interface IEmailSender
{
    Task SendRecoveryEmailAsync(RecoveryEmailMessage message, CancellationToken ct = default);

    /// <summary>
    /// Sends a tenant-invite email. Slice 6c.3 — stub on production
    /// transports (logs a warning + completes); integration tests use the
    /// <c>InMemoryCapturingEmailSender</c> to assert the message payload.
    /// </summary>
    Task SendTenantInviteAsync(TenantInviteEmailMessage message, CancellationToken ct = default);
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

/// <summary>
/// Value object that carries everything an email transport needs to compose
/// a tenant-invite email. Slice 6c.3 — the invite is a "join the workspace"
/// link with no credential embedded; the user sets their own password on
/// first login. <see cref="InvitationToken"/> is opaque + single-use.
/// </summary>
public sealed record TenantInviteEmailMessage(
    string To,
    string? DisplayName,
    string TenantName,
    Guid InvitationToken,
    DateTimeOffset ExpiresAt);