using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Portal;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Portal;

/// <summary>
/// crm-complete F2 (D5). Portal clients only (403 otherwise). A fixed number of statements: the client row,
/// its active contacts, the login (Identity), its active projects and their progress (Projects), and the open
/// periods. Contact notes, client notes, job title, marital status and the project owner are never read into
/// the response.
/// </summary>
internal sealed class GetMyPortalProfileQueryHandler(
    CrmDbContext db, ICrmAccessGuard guard, IUserDirectory users, IProjectDirectory projectDirectory)
    : IQueryHandler<GetMyPortalProfileQuery, PortalProfileResponse>
{
    public async Task<Result<PortalProfileResponse>> HandleAsync(GetMyPortalProfileQuery query, CancellationToken ct = default)
    {
        Result<CrmActor> actor = await guard.ResolveActorAsync(query.AppUserId, ct);
        if (actor.IsFailure)
        {
            return Result.Failure<PortalProfileResponse>(actor.Error);
        }

        if (actor.Value!.Kind != CrmActorKind.Client)
        {
            return Result.Failure<PortalProfileResponse>(CrmPortalErrors.NotAClient);
        }

        Guid clientId = actor.Value.ClientId!.Value;
        var client = await db.Clients.AsNoTracking()
            .Where(c => c.Id == clientId)
            .Select(c => new { c.Id, c.AppUserId, c.Address })
            .FirstOrDefaultAsync(ct);
        if (client is null)
        {
            return Result.Failure<PortalProfileResponse>(CrmPortalErrors.NotFound);
        }

        List<PortalProfileContactResponse> contacts = await db.CrmContacts.AsNoTracking()
            .Where(c => c.ClientId == clientId && c.IsActive)
            .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.LastName).ThenBy(c => c.FirstName)
            .Select(c => new PortalProfileContactResponse(
                c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.Mobile, CrmEnumMapping.ToContracts(c.Role), c.IsPrimary))
            .ToListAsync(ct);

        UserSummary? login = await users.GetAsync(client.AppUserId, ct);
        PortalProfileContactResponse? primary = contacts.FirstOrDefault(c => c.IsPrimary);
        string displayName = login?.DisplayName?.Trim() ?? string.Empty;
        string[] parts = displayName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var identity = new PortalClientIdentityResponse(
            client.Id,
            primary is null ? displayName : $"{primary.FirstName} {primary.LastName}".Trim(),
            primary?.FirstName ?? (parts.Length > 0 ? parts[0] : string.Empty),
            primary?.LastName ?? (parts.Length > 1 ? parts[1] : string.Empty),
            login?.Email,
            primary?.Phone ?? primary?.Mobile,
            client.Address);

        var projects = (await projectDirectory.GetProjectsByClientAsync(clientId, ct)).Where(p => p.IsActive).ToList();
        List<Guid> projectIds = [.. projects.Select(p => p.Id)];
        var progress = (await projectDirectory.GetProgressAsync(projectIds, ct))
            .ToDictionary(p => p.ProjectId);
        Dictionary<Guid, Guid> openPeriods = projectIds.Count == 0
            ? []
            : (await db.CrmRequirementPeriods.AsNoTracking()
                .Where(p => projectIds.Contains(p.ProjectId) && p.Status == Crm.Domain.RequirementPeriodStatus.Open && p.IsActive)
                .Select(p => new { p.ProjectId, p.Id, p.ClosesAtUtc })
                .ToListAsync(ct))
                .GroupBy(p => p.ProjectId)
                .ToDictionary(g => g.Key, g => g.OrderBy(p => p.ClosesAtUtc).First().Id);

        List<PortalProfileProjectResponse> projectRows = [.. projects
            .OrderBy(p => p.Name)
            .Select(p =>
            {
                ProjectProgressSummary? pr = progress.GetValueOrDefault(p.Id);
                return new PortalProfileProjectResponse(
                    p.Id, p.Code, p.Name, p.Status.ToString(), pr?.ProgressPercentage ?? 0m, pr?.ActiveStageId, pr?.ActiveStageName,
                    openPeriods.TryGetValue(p.Id, out Guid periodId) ? periodId : null);
            })];

        return new PortalProfileResponse(identity, contacts, projectRows);
    }
}
