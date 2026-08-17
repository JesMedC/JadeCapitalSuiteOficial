using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Contracts.Journal;
using JadeCapital.Trading.Domain.Journal;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Journal.CreateOrUpdate;

/// <summary>
/// Command: upsert del journal entry del usuario para una fecha local
/// (<c>POST /api/journal/today</c> o path-parameterizado por fecha).
///
/// Si ya existe un entry para <c>(userId, localDate)</c>, el handler lo
/// carga y aplica <see cref="JournalEntry.Update"/> in-place; sino,
/// construye uno nuevo via <see cref="JournalEntry.CreateOrUpdate"/>.
/// La DB unique index <c>ux_journal_user_date</c> es la red de
/// seguridad contra race conditions.
///
/// Se mapea a <see cref="JournalEntryDto"/>: el FE recibe el entry
/// upserted con todos los campos (incluso los no enviados en esta
/// request, si los tenia de antes).
/// </summary>
public sealed record CreateOrUpdateJournalEntryCommand(
    Guid UserId,
    LocalDate LocalDate,
    string Timezone,
    byte? MoodPre,
    byte? MoodDuring,
    byte? MoodPost,
    string? PremarketPlan,
    string? PostmarketReflection,
    string[]? Tags) : IRequest<Result<JournalEntryDto>>;

/// <summary>
/// Validacion FluentValidation: los rangos y caps los enforce tambien el
/// aggregate (defense in depth), pero el validator produce un 400
/// estructurado con <see cref="FluentValidation.ValidationException"/>
/// ANTES de llegar al dominio. Mantiene los dos paths (validator para
/// shape errors, dominio para invariants) consistentes con el resto
/// del modulo (ver <c>CreateOrUpdateTradeReviewValidator</c>).
/// </summary>
public sealed class CreateOrUpdateJournalEntryValidator : AbstractValidator<CreateOrUpdateJournalEntryCommand>
{
    public CreateOrUpdateJournalEntryValidator()
    {
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        RuleFor(x => x.Timezone).NotEmpty();
        RuleFor(x => x.MoodPre).Must(m => m is null || (m >= 1 && m <= 5))
            .WithMessage("MoodPre must be between 1 and 5 when present.");
        RuleFor(x => x.MoodDuring).Must(m => m is null || (m >= 1 && m <= 5))
            .WithMessage("MoodDuring must be between 1 and 5 when present.");
        RuleFor(x => x.MoodPost).Must(m => m is null || (m >= 1 && m <= 5))
            .WithMessage("MoodPost must be between 1 and 5 when present.");
        RuleFor(x => x.PremarketPlan).MaximumLength(JournalEntry.MaxPremarketPlanLength);
        RuleFor(x => x.PostmarketReflection).MaximumLength(JournalEntry.MaxPostmarketReflectionLength);
        RuleFor(x => x.Tags).Must(t => t is null || t.Length <= JournalEntry.MaxTagsCount)
            .WithMessage($"Tags must contain at most {JournalEntry.MaxTagsCount} items.");
        RuleFor(x => x.Tags).Must(t => t is null || t.All(tag => tag is null || tag.Length <= JournalEntry.MaxTagLength))
            .WithMessage($"Each tag must be at most {JournalEntry.MaxTagLength} characters.");
    }
}

/// <summary>
/// Upsert handler. Lookup por (userId, localDate) primero; si no
/// existe, build con <see cref="JournalEntry.CreateOrUpdate"/>; si
/// existe, build via <see cref="JournalEntry.Update"/> in-place.
/// El domain enforce las mismas validaciones que el validator — los
/// errores se propagan via <see cref="Result{T}"/> y el endpoint los
/// mapea a ProblemDetails.
/// </summary>
public sealed class CreateOrUpdateJournalEntryHandler
    : IRequestHandler<CreateOrUpdateJournalEntryCommand, Result<JournalEntryDto>>
{
    private readonly IJournalEntryRepository _entries;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public CreateOrUpdateJournalEntryHandler(
        IJournalEntryRepository entries,
        IUnitOfWork uow,
        IClock clock)
    {
        _entries = entries;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<JournalEntryDto>> Handle(
        CreateOrUpdateJournalEntryCommand req,
        CancellationToken ct)
    {
        // Upsert path: si ya existe, update in-place; sino, create.
        var existing = await _entries.GetByUserAndDateAsync(req.UserId, req.LocalDate, ct);

        // Conversion de byte? -> Mood? via FromTrusted (el validator
        // ya valido el rango 1..5 si el byte estaba presente).
        var moodPre = req.MoodPre.HasValue ? Mood.FromTrusted(req.MoodPre.Value) : (Mood?)null;
        var moodDuring = req.MoodDuring.HasValue ? Mood.FromTrusted(req.MoodDuring.Value) : (Mood?)null;
        var moodPost = req.MoodPost.HasValue ? Mood.FromTrusted(req.MoodPost.Value) : (Mood?)null;
        IReadOnlyList<string>? tags = req.Tags;

        JournalEntry entry;
        if (existing is null)
        {
            var createResult = JournalEntry.CreateOrUpdate(
                userId: req.UserId,
                localDate: req.LocalDate,
                timezone: req.Timezone,
                moodPre: moodPre,
                moodDuring: moodDuring,
                moodPost: moodPost,
                premarketPlan: req.PremarketPlan,
                postmarketReflection: req.PostmarketReflection,
                tags: tags,
                clock: _clock);

            if (createResult.IsFailure)
                return Result.Failure<JournalEntryDto>(createResult.Error);

            entry = createResult.Value;
            await _entries.AddAsync(entry, ct);
        }
        else
        {
            var updateResult = existing.Update(
                moodPre: moodPre,
                moodDuring: moodDuring,
                moodPost: moodPost,
                premarketPlan: req.PremarketPlan,
                postmarketReflection: req.PostmarketReflection,
                tags: tags,
                clock: _clock);

            if (updateResult.IsFailure)
                return Result.Failure<JournalEntryDto>(updateResult.Error);

            entry = existing;
            await _entries.UpdateAsync(entry, ct);
        }

        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
            return Result.Failure<JournalEntryDto>(saved.Error);

        return Result.Success(entry.ToDto());
    }
}
