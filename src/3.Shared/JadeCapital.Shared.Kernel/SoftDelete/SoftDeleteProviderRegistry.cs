using JadeCapital.Shared.Kernel.SoftDelete;

namespace JadeCapital.Shared.Kernel.SoftDelete;

/// <summary>
/// Default in-memory implementation of <see cref="ISoftDeleteProviderRegistry"/>
/// (Wave 6, slice 6d.1).
///
/// <para>
/// The constructor takes the full set of <see cref="ISoftDeleteProvider"/>
/// instances — DI injects them as <c>IEnumerable&lt;ISoftDeleteProvider&gt;</c>
/// from every registered provider across every module. Lookup is
/// case-sensitive exact-match.
/// </para>
///
/// <para>
/// <b>Why in-memory is fine</b>: the provider list is fixed at
/// composition-time (no runtime registration). A <c>Dictionary</c> keyed
/// by <see cref="ISoftDeleteProvider.EntityType"/> gives O(1) lookup with
/// no allocation cost on the hot path.
/// </para>
/// </summary>
public sealed class SoftDeleteProviderRegistry : ISoftDeleteProviderRegistry
{
    private readonly IReadOnlyDictionary<string, ISoftDeleteProvider> _providers;

    public SoftDeleteProviderRegistry(IEnumerable<ISoftDeleteProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var dict = new Dictionary<string, ISoftDeleteProvider>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            if (string.IsNullOrEmpty(provider.EntityType))
                throw new ArgumentException(
                    $"ISoftDeleteProvider '{provider.GetType().FullName}' exposes a null/empty EntityType.",
                    nameof(providers));

            // Defensive: two providers for the same entity type is a
            // configuration error — fail fast at startup rather than
            // silently picking one.
            if (dict.ContainsKey(provider.EntityType))
                throw new InvalidOperationException(
                    $"Duplicate ISoftDeleteProvider registration for entity type '{provider.EntityType}'.");

            dict[provider.EntityType] = provider;
        }
        _providers = dict;
    }

    public ISoftDeleteProvider? GetByEntityType(string entityType)
        => _providers.TryGetValue(entityType, out var provider) ? provider : null;
}
