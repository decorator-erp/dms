namespace Crm.Infrastructure.Persistence.Migrations.InitialCrm;

/// <summary>
/// crm-complete F6: the guard of <see cref="CrmCompleteCrm"/>, run before any DDL. It refuses a database that
/// already has the storefront tables or the brief-space <c>Selections</c> column (a half-applied or hand-patched
/// schema) instead of failing halfway. The migration is additive only: two new tables and one nullable column;
/// nothing is dropped or rewritten.
/// </summary>
internal static class CrmCompleteCrmSql
{
    public const string Guard =
        """
        DO $$
        BEGIN
            IF to_regclass('crm.crm_space_selections') IS NOT NULL
               OR to_regclass('crm.crm_space_selection_options') IS NOT NULL THEN
                RAISE EXCEPTION 'crm-complete: the storefront tables already exist; refusing to re-create them';
            END IF;
            IF EXISTS (SELECT 1 FROM information_schema.columns
                       WHERE table_schema = 'crm' AND table_name = 'crm_brief_spaces' AND column_name = 'Selections') THEN
                RAISE EXCEPTION 'crm-complete: crm.crm_brief_spaces."Selections" already exists';
            END IF;
        END $$;
        """;
}
