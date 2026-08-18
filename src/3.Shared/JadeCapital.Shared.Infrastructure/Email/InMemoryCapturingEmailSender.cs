using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Shared.Infrastructure.Email;

/// <summary>
/// In-process sender that captures every <see cref="RecoveryEmailMessage"/>
/// into a thread-safe buffer for assertions in integration tests. The body
/// (including the temporary password) is NEVER logged — by contract, the
/// slice-0c <c>InMemorySender_NeverLogsBody</c> test inspects the buffer,
/// not the log stream.
/// </summary>
public sealed class InMemoryCapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<RecoveryEmailMessage> _captured = new();
    private readonly ConcurrentQueue<TenantInviteEmailMessage> _invitesCaptured = new();
    private readonly ILogger<InMemoryCapturingEmailSender> _logger;

    public InMemoryCapturingEmailSender(ILogger<InMemoryCapturingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendRecoveryEmailAsync(RecoveryEmailMessage message, CancellationToken ct = default)
    {
        _captured.Enqueue(message);
        // Correlation only — no message body, no temporary password, no hash.
        _logger.LogInformation("Recovery email captured for {To} (message id only).", message.To);
        return Task.CompletedTask;
    }

    public Task SendTenantInviteAsync(TenantInviteEmailMessage message, CancellationToken ct = default)
    {
        _invitesCaptured.Enqueue(message);
        // Correlation only — no token, no link.
        _logger.LogInformation("Tenant invite captured for {To} (tenant {TenantName}).",
            message.To, message.TenantName);
        return Task.CompletedTask;
    }

    /// <summary>Snapshot of every captured recovery message. Tests assert against this.</summary>
    public IReadOnlyList<RecoveryEmailMessage> Captured => _captured.ToArray();

    /// <summary>Snapshot of every captured tenant-invite message (slice 6c.3).</summary>
    public IReadOnlyList<TenantInviteEmailMessage> CapturedInvites => _invitesCaptured.ToArray();

    public void Reset()
    {
        _captured.Clear();
        _invitesCaptured.Clear();
    }
}