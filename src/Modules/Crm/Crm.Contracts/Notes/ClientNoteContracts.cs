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

/// <summary>C-8 (LC-I1): only the note's author (<see cref="ActingEmployeeId"/>) or a
/// <c>crm.notes.manage_all</c> holder may change it; anyone else is 403 <c>crm.notes.NotAuthor</c>.</summary>
public sealed record UpdateCrmClientNoteCommand(Guid NoteId, string Content, Guid ActingEmployeeId, string UserId) : ICommand, ICrmClientScoped
{
    CrmClientScopeTarget ICrmClientScoped.ScopeTarget => CrmClientScopeTarget.Note(NoteId);
}

/// <summary>C-8 (LC-I1): the same author-or-<c>manage_all</c> rule as <see cref="UpdateCrmClientNoteCommand"/>.</summary>
public sealed record DeleteCrmClientNoteCommand(Guid NoteId, Guid ActingEmployeeId, string UserId) : ICommand, ICrmClientScoped
{
    CrmClientScopeTarget ICrmClientScoped.ScopeTarget => CrmClientScopeTarget.Note(NoteId);
}

public sealed record GetCrmClientNotesByClientQuery(Guid ClientId, string UserId) : IQuery<IReadOnlyList<CrmClientNoteResponse>>, ICrmClientScoped
{
    CrmClientScopeTarget ICrmClientScoped.ScopeTarget => CrmClientScopeTarget.Client(ClientId);
}
