namespace BuildingBlocks.Auditing;

/// <summary>
/// What an audit row records. Three members, because three is exactly what the audit mechanism can
/// produce: <c>AuditSaveChangesInterceptor</c> maps <c>EntityState.Added/Modified/Deleted</c> and has
/// no other input.
///
/// <para><b>This is the half of "B18 closed by mechanism" that lives in the type system.</b> The
/// backend's enum has ~400 members (<c>backend/Domain/Enums/AuditAction.cs</c>) because it served a
/// hand-call API — <c>LogActionAsync(AuditAction.CrmCatalogItemMoved, "CrmCatalogItem", …)</c> — where
/// the action, the entity name and the id were all literals chosen at the call site. That is precisely
/// the mechanism that produced B18. Porting the 400 members would ship 397 unreachable values, and an
/// unreachable member is not inert: it advertises the API that caused the defect, and someone
/// eventually uses it. With three members and an <c>internal AuditLog.Record</c> factory whose only
/// call site is the interceptor, a wrong value is unrepresentable rather than merely discouraged.</para>
///
/// <para><b>Deliberately absent: <c>Read</c>, and the auth actions.</b> A read mutates nothing, so the
/// interceptor never sees it; <c>Login</c>/<c>Logout</c>/<c>TokenRefreshed</c>/<c>TokenRevoked</c> were
/// factory methods passing <c>nameof(ApplicationUser)</c> as a literal — a type BuildingBlocks cannot
/// see — and are redundant besides, since a successful login updates <c>LastLoginAt</c> and therefore
/// already produces an <see cref="Update"/> row with the column named in <c>ChangedColumns</c>. What
/// this genuinely loses is the FAILED login, which changes no entity and so writes no row. That is
/// recorded in the deferred table as a separate mechanism, not papered over here.</para>
///
/// <para>Lives in BuildingBlocks rather than <c>Notifications.Contracts</c> (where PLAN-PHASE6.md §3
/// placed it) for the only reason that matters: <c>AuditLog</c> lives here, and
/// <c>Notifications.Contracts</c> references BuildingBlocks — so an <c>AuditAction</c> over there
/// would make <c>AuditLog.Action</c> close a reference cycle. Notifications gets it transitively.</para>
/// </summary>
public enum AuditAction
{
    None = 0,
    Create = 1,
    Update = 2,
    Delete = 3,
}
