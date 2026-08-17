namespace JadeCapital.Shared.Kernel.Realtime;

// ============================================================================
//  IQuoteClient — slice 4c (Realtime) server-side SignalR client contract.
//
//  Mirrors the FE @microsoft/signalr client interface. The server invokes
//  methods on this interface; SignalR generates the strongly-typed proxy
//  on the FE side (no manual JSON shape duplication).
//
//  Lives under Shared.Kernel.Realtime (not Trading.Api) because the FE
//  needs the type-name-only contract to wire up its callback registration
//  in @microsoft/signalr's HubConnectionBuilder.withUrl(...).withAutomaticReconnect().
//  Keeping it kernel-level avoids dragging Trading.Api into FE type deps.
// ============================================================================

public interface IQuoteClient
{
    /// <summary>
    /// Server invokes this when a quote tick for a subscribed symbol fires.
    /// Recipients MUST tolerate out-of-order delivery (network jitter) and
    /// stale ticks (no-op if the tick timestamp is older than the local copy).
    /// </summary>
    Task OnQuoteUpdate(QuoteUpdate update);

    /// <summary>
    /// Server invokes this on protocol-level errors (invalid symbol, hub
    /// misconfiguration, broadcast failure). Codes are stable strings
    /// (e.g. "invalid.symbol", "hub.unavailable") so the FE can react.
    /// </summary>
    Task OnError(string code, string message);
}