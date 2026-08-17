using JadeCapital.Trading.Domain.Ai;

namespace JadeCapital.Trading.Application.Abstractions;

// ============================================================================
//  ICoachingPromptRepository — slice 5b.2 (Wave 5).
//
//  Persistence abstraction for the <see cref="CoachingPrompt"/> aggregate.
//  Cross-user isolation lives in the handlers (the GetAiCoachingPromptsHandler
//  filters by UserId; the BG service iterates every active user from the
//  IUserTradingContextProvider).
// ============================================================================

public interface ICoachingPromptRepository
{
    /// <summary>Append. The aggregate owns the id; do NOT pre-assign.</summary>
    Task AddAsync(CoachingPrompt prompt, CancellationToken ct);

    /// <summary>
    /// Returns the most-recent AI prompt for the user generated on the
    /// given UTC calendar date. Used by <c>GenerateCoachingPromptHandler</c>
    /// to enforce the "one prompt per user per UTC day" invariant.
    /// </summary>
    Task<CoachingPrompt?> FindByUserAndDateAsync(
        Guid userId,
        DateTimeOffset dayUtc,
        CancellationToken ct);

    /// <summary>
    /// Lists the user's AI prompts in the [from, to) window, sorted by
    /// <c>createdAt DESC</c>. Used by <c>GetAiCoachingPromptsHandler</c>.
    /// The window bounds are inclusive of <paramref name="from"/>, exclusive
    /// of <paramref name="to"/> — matches the Wave 2b/3b semantic.
    /// </summary>
    Task<IReadOnlyList<CoachingPrompt>> ListByUserAndWindowAsync(
        Guid userId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct);
}