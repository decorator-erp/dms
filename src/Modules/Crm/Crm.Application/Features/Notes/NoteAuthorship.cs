using BuildingBlocks;
using Crm.Contracts;
using Crm.Domain;
using SharedKernel;

namespace Crm.Application.Features.Notes;

/// <summary>C-8 (LC-I1): the owner decided a note is changed only by its author. A
/// <see cref="CrmPermissions.Notes.ManageAll"/> holder (admin and manager get it through
/// <c>RoleClaimSeeder</c>) may change anyone's.</summary>
internal static class NoteAuthorship
{
    public static Result EnsureMayChange(CrmClientNote note, Guid actingEmployeeId, ICurrentUser currentUser) =>
        note.WrittenById == actingEmployeeId || currentUser.HasPermission(CrmPermissions.Notes.ManageAll)
            ? Result.Success()
            : CrmClientNoteErrors.NotAuthor;
}
