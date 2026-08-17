using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Journal.Delete;

/// <summary>
/// Command: hard delete de un journal entry por id — <c>DELETE /api/journal/{id}</c>.
///
/// Cross-user scope: el handler busca con FindByIdAsync(entryId, userId)
/// que filtra WHERE id = @entryId AND user_id = @userId. Si no existe
/// el entry o pertenece a otro user, colapsa a 404 para no enumerar
/// IDs ajenos.
///
/// Hard delete en Wave 2 (la DB es la fuente de verdad). Soft-delete
/// se difiere a Fase 6.
/// </summary>
public sealed record DeleteJournalEntryCommand(
    Guid EntryId,
    Guid UserId) : IRequest<Result>;

/// <summary>
/// Delete handler. FindByIdAsync(entryId, userId) primero para
/// cross-user scope; si null, 404. Si lo encuentra, DeleteAsync
/// (sin filtro de user — ya validamos arriba) + SaveChanges.
/// </summary>
public sealed class DeleteJournalEntryHandler : IRequestHandler<DeleteJournalEntryCommand, Result>
{
    private readonly IJournalEntryRepository _entries;
    private readonly IUnitOfWork _uow;

    public DeleteJournalEntryHandler(IJournalEntryRepository entries, IUnitOfWork uow)
    {
        _entries = entries;
        _uow = uow;
    }

    public async Task<Result> Handle(DeleteJournalEntryCommand req, CancellationToken ct)
    {
        var entry = await _entries.FindByIdAsync(req.EntryId, req.UserId, ct);
        if (entry is null)
            return Result.Failure(TradingDomainErrors.Journal.NotFound);

        await _entries.DeleteAsync(req.EntryId, ct);

        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
            return Result.Failure(saved.Error);

        return Result.Success();
    }
}
