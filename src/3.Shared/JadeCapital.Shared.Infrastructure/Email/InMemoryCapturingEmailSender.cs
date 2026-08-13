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

    /// <summary>Snapshot of every captured message. Tests assert against this.</summary>
    public IReadOnlyList<RecoveryEmailMessage> Captured => _captured.ToArray();

    public void Reset() => _captured.Clear();
}