# 9. Bespoke vs generic audit decorators

## Status
Accepted (2026-08-19, Wave 10)

## Context
Each of the 19 audit decorators has a bespoke shape (per aggregate). Is there a generic `DecoratedRepository<T>` that subsumes them all?

## Decision
- Wave 7 7a.0 REFACTOR: moved `DecoratedRepository<T>` from Identity.Infrastructure to Shared.Infrastructure to enable cross-module reuse
- BUT every Wave 7/8/9 decorator ended up bespoke anyway (with bespoke tests)
- Bespoke is correct because:
  - Aggregate-specific mutations: Trade has Open/Close; Strategy has MarkSuperseded; Subscription has Cancel/ExtendTrial
  - Cross-tenant semantics differ: User is single-tenant but Trade is per-Account
  - Diff shapes vary: Trade diffs in price/quantity; Subscription diffs in PlanTier
- Generic `DecoratedRepository<T>` is only useful for purely-CRU aggregates (1 of 19)

## Consequences
- Pros: each decorator encodes the aggregate's semantics + spec scenarios explicitly
- Cons: more code per aggregate (avg 200-400 LOC per decorator + tests)
- The 7a.0 refactor was wasted effort — but `DecoratedRepository<T>` is still available for any future purely-CRU aggregate
