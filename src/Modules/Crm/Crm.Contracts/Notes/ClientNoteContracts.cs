using BuildingBlocks.Messaging;

namespace Crm.Contracts.Notes;

/// <summary>Internal-only — never surfaced on the Portal. <c>CrmClientNote</c>'s own doc comment says
/// the backend's access layer denies contact actors outright; the endpoint layer (G9-14) enforces the
/// same by never registering a Portal route for this feature.</summary>
public sealed record CrmClientNoteResponse(
    Guid Id, Guid ClientId, Guid? ProjectId, string Content, Guid WrittenById);

public sealed record CreateCrmClientNoteCommand(
    Guid ClientId, Guid? ProjectId, string Content, Guid WrittenById, string UserId) : ICommand<Guid>, ICrmClientScoped
{
    CrmClientScopeTarget ICrmClientScoped.ScopeTarget => CrmClientScopeTarget.Client(ClientId);
}

public sealed record UpdateCrmClientNoteCommand(Guid NoteId, string Content, string UserId) : ICommand, ICrmClientScoped
{
    CrmClientScopeTarget ICrmClientScoped.ScopeTarget => CrmClientScopeTarget.Note(NoteId);
}

public sealed record DeleteCrmClientNoteCommand(Guid NoteId, string UserId) : ICommand, ICrmClientScoped
{
    CrmClientScopeTarget ICrmClientScoped.ScopeTarget => CrmClientScopeTarget.Note(NoteId);
}

public sealed record GetCrmClientNotesByClientQuery(Guid ClientId, string UserId) : IQuery<IReadOnlyList<CrmClientNoteResponse>>, ICrmClientScoped
{
    CrmClientScopeTarget ICrmClientScoped.ScopeTarget => CrmClientScopeTarget.Client(ClientId);
}
