using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Domain.Common;

namespace JadeCapital.Trading.Domain.Journal;

/// <summary>
/// Aggregate Root del journal daily (slice 2a.1).
///
/// Reglas de negocio:
/// <list type="bullet">
///   <item>Exactly one entry per <c>(user_id, local_date)</c> — enforced
///   por UNIQUE INDEX <c>ux_journal_user_date</c> en la DB. La API no
///   expone UPDATE del entry completo: <see cref="CreateOrUpdate"/> es
///   upsert atómico (si existe entry para esa fecha, el handler lo
///   carga y aplica <see cref="Update"/>; sino, crea uno nuevo).</item>
///   <item>La <c>local_date</c> NO se valida como Guid.Empty (es un
///   <see cref="LocalDate"/>, no un Guid). El handler de aplicacion es
///   responsable de computarla a partir del timezone del header
///   <c>X-User-Timezone</c>.</item>
///   <item><c>mood_pre</c>, <c>mood_during</c>, <c>mood_post</c> son
///   <see cref="Mood"/> nullable. El VO ya viene validado por
///   <see cref="Mood.Create"/>; el aggregate re-valida por si el
///   codepath de hydration los construye con un cast explicito.</item>
///   <item><c>premarket_plan</c> &lt;= 2000 chars; <c>postmarket_reflection</c>
///   &lt;= 5000 chars; <c>tags</c> hasta 10 items con cada uno
///   &lt;= 32 chars. La DB es la red de seguridad via CHECK constraints.</item>
///   <item>El cross-user scope se enforce desde Application: el handler
///   pasa el userId autenticado y la query <c>GetByUserAndDateAsync</c>
///   filtra WHERE user_id = @userId. El aggregate NO carga al User.</item>
///   <item>Si todos los campos opcionales son null/empty, el upsert
///   falla con <c>validation.journal.nothing_to_save</c> — esto evita
///   escrituras inútiles (e.g. double-click en el FE).</item>
/// </list>
/// </summary>
public sealed class JournalEntry : AggregateRoot<Guid>
{
    /// <summary>Max length del premarket_plan (libre, <= 2000 chars).</summary>
    public const int MaxPremarketPlanLength = 2000;

    /// <summary>Max length del postmarket_reflection (<= 5000 chars).</summary>
    public const int MaxPostmarketReflectionLength = 5000;

    /// <summary>Max length de cada tag individual (<= 32 chars).</summary>
    public const int MaxTagLength = 32;

    /// <summary>Max cantidad de tags por entry (<= 10).</summary>
    public const int MaxTagsCount = 10;

    public Guid UserId { get; private set; }
    public LocalDate LocalDate { get; private set; }
    public string Timezone { get; private set; } = default!;

    public Mood? MoodPre { get; private set; }
    public Mood? MoodDuring { get; private set; }
    public Mood? MoodPost { get; private set; }

    public string? PremarketPlan { get; private set; }
    public string? PostmarketReflection { get; private set; }

    /// <summary>
    /// Lista de tags del entry. Nunca null — si el usuario no proveyo
    /// tags o todos estaban vacíos, es una lista vacía. EF persiste
    /// la lista vacía como NULL en la columna <c>tags TEXT[]</c> (ver
    /// <see cref="JadeCapital.Trading.Infrastructure.Persistence.Configurations.JournalEntryConfiguration"/>).
    /// </summary>
    public IReadOnlyList<string> Tags { get; private set; } = Array.Empty<string>();

    // EF Core.
    private JournalEntry() { }

    private JournalEntry(
        Guid id,
        Guid userId,
        LocalDate localDate,
        string timezone,
        Mood? moodPre,
        Mood? moodDuring,
        Mood? moodPost,
        string? premarketPlan,
        string? postmarketReflection,
        IReadOnlyList<string> tags,
        IClock clock) : base(id)
    {
        UserId = userId;
        LocalDate = localDate;
        Timezone = timezone;
        MoodPre = moodPre;
        MoodDuring = moodDuring;
        MoodPost = moodPost;
        PremarketPlan = premarketPlan;
        PostmarketReflection = postmarketReflection;
        Tags = tags;
        // Override CreatedAt del constructor base (UtcNow) para que el
        // test pueda fijar el reloj y CreatedAt == clock.UtcNow.
        var now = clock.UtcNow;
        SetCreatedAt(now);
        UpdatedAt = now;
    }

    /// <summary>
    /// Construye un nuevo journal entry para la fecha local del usuario.
    /// El handler de aplicacion debe llamar antes a
    /// <see cref="IJournalEntryRepository.GetByUserAndDateAsync"/> para
    /// detectar upsert: si la query retorna un entry existente, el
    /// handler debe invocar <see cref="Update"/> en lugar de
    /// <see cref="CreateOrUpdate"/>.
    ///
    /// Validaciones (defense in depth — el VO Mood ya viene validado):
    /// <list type="number">
    ///   <item><paramref name="userId"/> != Guid.Empty.</item>
    ///   <item><paramref name="timezone"/> no vacio (trimmed).</item>
    ///   <item><paramref name="premarketPlan"/> &lt;= 2000 chars.</item>
    ///   <item><paramref name="postmarketReflection"/> &lt;= 5000 chars.</item>
    ///   <item><paramref name="tags"/> count &lt;= 10 y cada uno &lt;= 32 chars.</item>
    ///   <item>Al menos un campo opcional no-vacio (mood / plan / reflection / tags).</item>
    /// </list>
    /// En exito emite un <see cref="JournalEntryCreatedDomainEvent"/>.
    /// </summary>
    public static Result<JournalEntry> CreateOrUpdate(
        Guid userId,
        LocalDate localDate,
        string timezone,
        Mood? moodPre,
        Mood? moodDuring,
        Mood? moodPost,
        string? premarketPlan,
        string? postmarketReflection,
        IReadOnlyList<string>? tags,
        IClock clock)
    {
        if (userId == Guid.Empty)
            return Result.Failure<JournalEntry>(TradingDomainErrors.Journal.UserIdRequired);

        if (string.IsNullOrWhiteSpace(timezone))
            return Result.Failure<JournalEntry>(TradingDomainErrors.Journal.TimezoneRequired);

        var normalizedTimezone = timezone.Trim();

        var normalizedPlan = NormalizeText(premarketPlan);
        if (normalizedPlan is { Length: > MaxPremarketPlanLength })
            return Result.Failure<JournalEntry>(TradingDomainErrors.Journal.PremarketPlanTooLong);

        var normalizedReflection = NormalizeText(postmarketReflection);
        if (normalizedReflection is { Length: > MaxPostmarketReflectionLength })
            return Result.Failure<JournalEntry>(TradingDomainErrors.Journal.PostmarketReflectionTooLong);

        var normalizedTagsResult = NormalizeTags(tags);
        if (normalizedTagsResult.IsFailure)
            return Result.Failure<JournalEntry>(normalizedTagsResult.Error);

        var normalizedTags = normalizedTagsResult.Value;

        // NothingToSave: el upsert rechaza cuando todos los campos
        // opcionales son null/empty (double-click guard).
        if (moodPre is null && moodDuring is null && moodPost is null
            && normalizedPlan is null && normalizedReflection is null
            && normalizedTags.Count == 0)
        {
            return Result.Failure<JournalEntry>(TradingDomainErrors.Journal.NothingToSave);
        }

        var id = Guid.NewGuid();
        var entry = new JournalEntry(
            id, userId, localDate, normalizedTimezone,
            moodPre, moodDuring, moodPost,
            normalizedPlan, normalizedReflection,
            normalizedTags, clock);

        entry.RaiseDomainEvent(new JournalEntryCreatedDomainEvent(
            entry.Id, entry.UserId, entry.LocalDate, clock.UtcNow));

        return Result.Success(entry);
    }

    /// <summary>
    /// Edita un entry existente (llamado por el handler cuando el repo
    /// encontro un row para <c>(user_id, local_date)</c>). Mismas
    /// validaciones que <see cref="CreateOrUpdate"/> salvo
    /// <c>nothing_to_save</c> — un Update vacio se permite (no-op
    /// explicito para refresh de timestamps en futuras iteraciones).
    ///
    /// En exito emite un <see cref="JournalEntryUpdatedDomainEvent"/>.
    /// </summary>
    public Result Update(
        Mood? moodPre,
        Mood? moodDuring,
        Mood? moodPost,
        string? premarketPlan,
        string? postmarketReflection,
        IReadOnlyList<string>? tags,
        IClock clock)
    {
        var normalizedPlan = NormalizeText(premarketPlan);
        if (normalizedPlan is { Length: > MaxPremarketPlanLength })
            return Result.Failure(TradingDomainErrors.Journal.PremarketPlanTooLong);

        var normalizedReflection = NormalizeText(postmarketReflection);
        if (normalizedReflection is { Length: > MaxPostmarketReflectionLength })
            return Result.Failure(TradingDomainErrors.Journal.PostmarketReflectionTooLong);

        var normalizedTagsResult = NormalizeTags(tags);
        if (normalizedTagsResult.IsFailure)
            return Result.Failure(normalizedTagsResult.Error);

        MoodPre = moodPre;
        MoodDuring = moodDuring;
        MoodPost = moodPost;
        PremarketPlan = normalizedPlan;
        PostmarketReflection = normalizedReflection;
        Tags = normalizedTagsResult.Value;
        UpdatedAt = clock.UtcNow;

        RaiseDomainEvent(new JournalEntryUpdatedDomainEvent(
            Id, UserId, clock.UtcNow));

        return Result.Success();
    }

    /// <summary>
    /// Hidratacion directa desde la DB (EF rehydration path). NO valida
    /// los rangos — la DB los enforce via CHECK constraints y los datos
    /// persistidos son por definicion validos. Usar SOLO desde
    /// <c>JournalEntryRepository</c>.
    /// </summary>
    public static JournalEntry Rehydrate(
        Guid id,
        Guid userId,
        LocalDate localDate,
        string timezone,
        Mood? moodPre,
        Mood? moodDuring,
        Mood? moodPost,
        string? premarketPlan,
        string? postmarketReflection,
        IReadOnlyList<string> tags,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        // El constructor usa un TrustedRehydrationClock para fijar
        // CreatedAt/UpdatedAt en createdAt inicialmente, y luego
        // overrideamos UpdatedAt con el valor real de la DB (que puede
        // diferir si el entry fue modificado despues del INSERT).
        var entry = new JournalEntry(
            id, userId, localDate, timezone,
            moodPre, moodDuring, moodPost,
            premarketPlan, postmarketReflection,
            tags,
            clock: new TrustedRehydrationClock(createdAt));
        entry.UpdatedAt = updatedAt;
        return entry;
    }

    /// <summary>
    /// Clock fake que solo se usa en el path de rehidratacion para que
    /// el constructor de <see cref="JournalEntry"/> reciba un IClock y
    /// pueda setear CreatedAt via SetCreatedAt. Las fechas reales
    /// vienen de la DB y se asignan explicitamente.
    /// </summary>
    private sealed class TrustedRehydrationClock : IClock
    {
        private readonly DateTimeOffset _now;
        public TrustedRehydrationClock(DateTimeOffset now) { _now = now; }
        public DateTimeOffset UtcNow => _now;
    }

    private static string? NormalizeText(string? input)
        => string.IsNullOrWhiteSpace(input) ? null : input.Trim();

    /// <summary>
    /// Normaliza la lista de tags: trimea cada uno, filtra vacíos,
    /// enforcea los caps (count + length por tag).
    /// </summary>
    private static Result<IReadOnlyList<string>> NormalizeTags(IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0)
            return Result.Success<IReadOnlyList<string>>(Array.Empty<string>());

        if (tags.Count > MaxTagsCount)
            return Result.Failure<IReadOnlyList<string>>(TradingDomainErrors.Journal.TooManyTags);

        var normalized = new List<string>(tags.Count);
        foreach (var raw in tags)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue; // skip empty

            var trimmed = raw.Trim();
            if (trimmed.Length > MaxTagLength)
                return Result.Failure<IReadOnlyList<string>>(TradingDomainErrors.Journal.TagTooLong);

            normalized.Add(trimmed);
        }

        return Result.Success<IReadOnlyList<string>>(normalized);
    }
}
