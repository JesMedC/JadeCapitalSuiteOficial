# Delta for Alerts

## MODIFIED Requirements

### Requirement: Five initial rules

#### `CurrentPriceNearStopRule`

- GIVEN the user has >= 1 open trade with `StopLossPrice` set
- AND the current quote for the trade's symbol (from `IQuoteProvider.GetQuoteAsync(symbol, ct)`) is within 1% of `StopLossPrice`
- WHEN evaluated
- THEN it MUST emit a `CurrentPriceNearStop` alert with severity `low` and copy: "Tu trade en {Symbol} está cerca de tu stop ({StopLossPrice}). Revisá si querés ajustarlo o cerrarlo manualmente."
- AND if `IQuoteProvider.GetQuoteAsync` returns `null` (symbol unknown or provider down), the rule MUST NOT emit the alert (silent skip, not an error)

(Previously: used `EntryPrice ± 1%` as proxy because Wave 3 had no market data provider. Now consumes `IQuoteProvider` (Wave 4b) for the real current price. Threshold (1%) and severity (low) are unchanged. Copy was "cerca de zona de entrada — sin tick data real"; new copy is honest about the proximity to the stop.)

#### Scenario: Open trade near stop — alert fires

- GIVEN user A has an open trade with `Symbol=EURUSD, StopLossPrice=1.0800, EntryPrice=1.0900`
- AND `IQuoteProvider.GetQuoteAsync("EURUSD")` returns `bid=1.0805, ask=1.0806` (within 1% of stop)
- WHEN the rule evaluates
- THEN the rule MUST emit a `CurrentPriceNearStop` alert with severity `low`
- AND the alert copy MUST reference `StopLossPrice = 1.0800` (NOT `EntryPrice`)

#### Scenario: Open trade far from stop — no alert

- GIVEN user A has an open trade with `Symbol=EURUSD, StopLossPrice=1.0800`
- AND `IQuoteProvider.GetQuoteAsync("EURUSD")` returns `bid=1.0950, ask=1.0951` (well above stop)
- WHEN the rule evaluates
- THEN the rule MUST NOT emit a `CurrentPriceNearStop` alert

#### Scenario: Provider returns null — silent skip

- GIVEN user A has an open trade with `Symbol=ZZZZZ` (unknown to provider)
- AND `IQuoteProvider.GetQuoteAsync("ZZZZZ")` returns `null`
- WHEN the rule evaluates
- THEN the rule MUST NOT emit a `CurrentPriceNearStop` alert
- AND the rule MUST NOT throw (silent skip is correct)

#### Scenario: Provider throws — rule continues

- GIVEN `IQuoteProvider.GetQuoteAsync` throws `ProviderUnavailableException`
- WHEN the rule evaluates
- THEN the rule MUST catch the exception and skip
- AND the alert MUST NOT be emitted
- AND the `AlertRegistry` MUST continue with the next rule (resilience)