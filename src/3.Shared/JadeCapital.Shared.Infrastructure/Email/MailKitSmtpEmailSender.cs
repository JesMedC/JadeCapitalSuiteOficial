using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace JadeCapital.Shared.Infrastructure.Email;

/// <summary>
/// MailKit SMTP transport. Used for production. Honors <see cref="MailOptions"/>
/// for host/port/credentials, STARTTLS, total per-attempt timeout, and retry
/// budget (initial + 2 retries, 250/750 ms jitter) per design.md.
///
/// Body composition is deliberately Spanish-only + Jade-branded per
/// identity-password-recovery spec; it never embeds the temporary password
/// in the subject or any log line.
/// </summary>
public class MailKitSmtpEmailSender : IEmailSender
{
    private readonly MailOptions _opts;
    private readonly ILogger<MailKitSmtpEmailSender> _logger;

    public MailKitSmtpEmailSender(IOptions<MailOptions> opts, ILogger<MailKitSmtpEmailSender> logger)
    {
        _opts = opts.Value;
        _logger = logger;
    }

    /// <summary>Constructor used by <see cref="MailpitSmtpEmailSender"/> to bake in localhost defaults.</summary>
    protected MailKitSmtpEmailSender(MailOptions opts, ILogger logger)
    {
        _opts = opts;
        _logger = (ILogger<MailKitSmtpEmailSender>)logger;
    }

    public async Task SendRecoveryEmailAsync(RecoveryEmailMessage message, CancellationToken ct = default)
    {
        var mime = BuildRecoveryMime(message, _opts.From);
        var attempt = 0;
        Exception? last = null;
        while (attempt < _opts.MaxAttempts)
        {
            attempt++;
            try
            {
                using var client = new SmtpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_opts.TimeoutMs);
                await client.ConnectAsync(_opts.Host, _opts.Port, _opts.UseStartTls, cts.Token);
                if (!string.IsNullOrEmpty(_opts.Username))
                    await client.AuthenticateAsync(_opts.Username, _opts.Password, cts.Token);
                await client.SendAsync(mime, cts.Token);
                await client.DisconnectAsync(true, cts.Token);
                _logger.LogInformation("Recovery email delivered (attempt {Attempt}, latency budget ok).", attempt);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                _logger.LogWarning("Recovery email attempt {Attempt} failed: {Error}", attempt, ex.GetType().Name);
                if (attempt < _opts.MaxAttempts)
                    await Task.Delay(attempt == 1 ? 250 : 750, ct);
            }
        }
        throw new InvalidOperationException(
            $"SMTP delivery failed after {_opts.MaxAttempts} attempts.", last);
    }

    /// <summary>Builds the Spanish Jade-branded recovery mime. Public for subclass override.</summary>
    protected static MimeMessage BuildRecoveryMime(RecoveryEmailMessage m, string from)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(m.To));
        message.Subject = "Recuperación de contraseña — Jade Capital";
        message.Body = new BodyBuilder
        {
            HtmlBody = $"""
                <p>Hola {System.Net.WebUtility.HtmlEncode(m.DisplayName)},</p>
                <p>Recibimos una solicitud para restablecer tu contraseña.</p>
                <p>Tu contraseña temporal es: <strong>{System.Net.WebUtility.HtmlEncode(m.TemporaryPassword)}</strong></p>
                <p>Caduca el {m.ExpiresAt:u}. Si no solicitaste este cambio, ignora este correo.</p>
                """,
            TextBody = $"""
                Hola {m.DisplayName},
                Tu contraseña temporal es: {m.TemporaryPassword}
                Caduca el {m.ExpiresAt:u}. Si no solicitaste este cambio, ignora este correo.
                """
        }.ToMessageBody();
        return message;
    }
}

/// <summary>
/// Local-development variant of <see cref="MailKitSmtpEmailSender"/> targeting
/// the Mailpit SMTP listener (defaults: <c>mailpit:1025</c> in docker compose,
/// <c>localhost:1025</c> in host dev). Honors <c>Mail__Host</c> /
/// <c>Mail__Port</c> from configuration when present; otherwise applies
/// sensible defaults. Wired via DI profile so the rest of the system sees a
/// normal SMTP transport.
/// </summary>
public sealed class MailpitSmtpEmailSender : MailKitSmtpEmailSender
{
    public MailpitSmtpEmailSender(IOptions<MailOptions> opts, ILogger<MailpitSmtpEmailSender> logger)
        : base(BuildOptions(opts), logger) { }

    private static MailOptions BuildOptions(IOptions<MailOptions> opts)
    {
        // Start from configuration so Mail__Host/Mail__Port env vars win.
        var configured = opts.Value;
        // Apply dev defaults only when config did not provide them.
        return new MailOptions
        {
            Host = string.IsNullOrWhiteSpace(configured.Host) ? "mailpit" : configured.Host,
            Port = configured.Port == 0 ? 1025 : configured.Port,
            Username = configured.Username,
            Password = configured.Password,
            From = configured.From,
            UseStartTls = configured.UseStartTls,
            TimeoutMs = configured.TimeoutMs,
            MaxAttempts = configured.MaxAttempts,
        };
    }
}