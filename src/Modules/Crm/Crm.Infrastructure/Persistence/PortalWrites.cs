using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel;

namespace Crm.Infrastructure.Persistence;

/// <summary>P2-A: save for the portal tables — <see cref="ArtifactWrites"/>' shape. A racing second active share of
/// the same item hits <see cref="ShareIndex"/> (409 <c>crm.portal.AlreadyShared</c>); a racing second settings row
/// hits <see cref="SettingsIndex"/> (409 <c>crm.portal.SettingsChanged</c>, safe to retry). Anything else throws.</summary>
internal static class PortalWrites
{
    public const string ShareIndex = "ux_crm_portal_shares_active_item";
    public const string SettingsIndex = "ux_crm_portal_settings_project";

    public static async Task<Result> SaveAsync(CrmDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: { } name,
        } && name is ShareIndex or SettingsIndex)
        {
            return name == ShareIndex ? CrmPortalShareErrors.AlreadyShared : CrmPortalShareErrors.SettingsChanged;
        }
    }
}
