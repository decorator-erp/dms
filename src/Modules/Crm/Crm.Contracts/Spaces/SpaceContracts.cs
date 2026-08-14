using BuildingBlocks.Messaging;

namespace Crm.Contracts.Spaces;

public sealed record CrmSpaceTypeResponse(
    Guid Id, string Code, string NameEn, string NameAr, int SortOrder, bool IsActive);

public sealed record CrmSpaceResponse(
    Guid Id, Guid ProjectId, Guid SpaceTypeId, string Name, string? Description, int SortOrder, bool IsActive);

public sealed record CreateCrmSpaceTypeCommand(string Code, string NameEn, string NameAr, int SortOrder = 0)
    : ICommand<Guid>;

public sealed record UpdateCrmSpaceTypeCommand(Guid SpaceTypeId, string NameEn, string NameAr, int SortOrder)
    : ICommand;

public sealed record SetCrmSpaceTypeActiveCommand(Guid SpaceTypeId, bool IsActive) : ICommand;

public sealed record CreateCrmSpaceCommand(
    Guid ProjectId, Guid SpaceTypeId, string Name, int SortOrder = 0, string? Description = null)
    : ICommand<Guid>;

public sealed record UpdateCrmSpaceCommand(Guid SpaceId, Guid SpaceTypeId, string Name, string? Description)
    : ICommand;

public sealed record SetCrmSpaceActiveCommand(Guid SpaceId, bool IsActive) : ICommand;

public sealed record GetCrmSpaceTypesQuery : IQuery<IReadOnlyList<CrmSpaceTypeResponse>>;

public sealed record GetCrmSpacesByProjectQuery(Guid ProjectId) : IQuery<IReadOnlyList<CrmSpaceResponse>>;
