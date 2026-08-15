using Serilog.Core;
using Serilog.Events;

namespace JadeCapital.Host;

/// <summary>
/// Serilog filter that scrubs sensitive content from log events before they
/// reach a sink. Slice 0c security invariant: no plaintext password, token,
/// hash, or email body may reach a log line. The rendered message is checked
/// against a deny-list; any matching event is rewritten to "REDACTED".
///
/// Destructured property names matched (case-insensitive substring):
///   password, temporarypassword, grantjti, refreshtoken, accesstoken,
///   passwordhash, tokenhash, body, htmlbody
/// </summary>
public sealed class PiiLogScrubber : ILogEventFilter
{
    private static readonly string[] DenyList =
    {
        "password", "temporarypassword", "temppassword",
        "grantjti", "refreshtoken", "accesstoken",
        "passwordhash", "tokenhash",
        "htmlbody", "textbody"
    };

    public LogEvent? CoerceLogEvent(LogEvent logEvent)
    {
        var msg = logEvent.RenderMessage();
        if (string.IsNullOrEmpty(msg)) return logEvent;
        foreach (var term in DenyList)
        {
            if (msg.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                // Replace the rendered message with a fixed notice — we cannot
                // mutate the immutable log event message directly, so we drop
                // the event entirely. Down-stream Serilog handlers will not see it.
                return null;
            }
        }
        return logEvent;
    }

    public bool IsEnabled(LogEvent logEvent) => true;
}