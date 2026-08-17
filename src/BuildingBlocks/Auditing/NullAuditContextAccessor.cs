namespace BuildingBlocks.Auditing;

/// <summary>
/// The fallback accessor: no ambient caller, ever. Registered by
/// <see cref="ModuleDbContextRegistration.AddModuleDbContext{TContext}"/> with <c>TryAdd</c>, so the
/// HTTP-backed implementation registered by <c>Api.AddApi</c> wins in the real host and this one only
/// takes effect where there is genuinely no request pipeline.
///
/// <para>That is not a corner case. The schema-migration fixtures
/// (<c>ApiFixture.MigrateModuleSchemaAsync</c> and <c>Rule9CrossSchemaForeignKeyTests</c>) build a bare
/// <c>ServiceCollection</c> through a module's own <c>IModule.Register</c> and never call
/// <c>AddApi</c> — this registration is what lets those throwaway providers resolve a DbContext at
/// all.</para>
/// </summary>
internal sealed class NullAuditContextAccessor : IAuditContextAccessor
{
    public AuditContext GetCurrent() => AuditContext.None;
}
