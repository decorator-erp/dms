using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Sites.Contracts;
using Sites.Contracts.Records;
using Sites.Domain;
using Sites.Infrastructure.Persistence;

namespace Sites.Infrastructure.Services;

/// <inheritdoc cref="ISiteReadQueries"/>
///
/// <remarks>
/// <para><b>Every identifier is quoted PascalCase.</b> The backend's originals are written in
/// snake_case because it maps every column explicitly; this solution has no such convention. An
/// unquoted <c>label</c> would fold to a lowercase identifier that does not exist — and it would fail
/// at runtime, on this report only, long after the build went green. Table and schema names are the
/// exception: those really are snake_case, set by <c>ToTable</c>.</para>
///
/// <para><b>The join onto <c>sites.sites</c> in both statements is a soft-delete check, not a
/// lookup</b> — nothing is selected from it. Raw SQL inherits no EF query filter, so without it a
/// deleted site still answers here while every other read of it 404s.</para>
/// </remarks>
internal sealed class SiteReadQueries(SitesDbContext db) : ISiteReadQueries
{
    private const string FamiliesSql = """
        WITH scoped AS (
            SELECT r."Id" AS record_id, r."Label" AS label, r."SpaceId" AS space_id
            FROM   sites.site_records r
            JOIN   sites.sites s ON s."Id" = r."SiteId" AND s."IsActive"
            WHERE  r."SiteId" = @siteId AND r."ModuleId" = @moduleId AND r."IsActive"
        ),
        key_items AS (
            SELECT i."Id" AS item_id, i."Code" AS item_code, i."Tolerance" AS tolerance, k.ord
            FROM   unnest(@groupByIds::uuid[]) WITH ORDINALITY AS k(item_id, ord)
            JOIN   sites.site_items i ON i."Id" = k.item_id
        ),
        parts AS (
            -- CROSS JOIN then LEFT JOIN, never a WHERE item_id = ANY(...): under the filtering form a
            -- record that never captured a key item produces NO row, so its signature is shorter and it
            -- silently merges into a different family. This way every record contributes exactly
            -- cardinality(@groupByIds) parts and a gap becomes an explicit -- its own family, which is
            -- what a surveyor needs to see.
            SELECT s.record_id, ki.ord, ki.item_code,
                   coalesce(
                       o."Code",
                       v."ValueText",
                       -- Snap to the item's own grain, in the item's own unit. The tolerance > 0 guard is
                       -- required, not defensive: tolerance is 0 for every non-Number item AND for
                       -- quantity, so an unguarded divide raises division_by_zero across half the
                       -- modules. trim_scale, because "ValueNumber" is numeric(18,4) and the snap
                       -- multiplies: without it a count reads 'quantity=1.0000'. The key is both the
                       -- value the tablet renders and the exporter's join token, so it must depend on
                       -- the measurement and not on a column's declared scale.
                       trim_scale(CASE WHEN ki.tolerance > 0
                             THEN round(v."ValueNumber" / ki.tolerance) * ki.tolerance
                             ELSE v."ValueNumber"
                        END)::text,
                       v."ValueBool"::text,
                       v."ValueDate"::text,
                       '/') AS key_part
            FROM       scoped s
            CROSS JOIN key_items ki
            LEFT JOIN  sites.site_record_values v ON v."RecordId" = s.record_id AND v."ItemId" = ki.item_id
            LEFT JOIN  sites.site_item_options  o ON o."ItemId" = v."ItemId" AND o."Id" = v."OptionId"
        ),
        signature AS (
            -- Two-stage aggregation: GROUP BY cannot take a variable column list, so string_agg
            -- manufactures one deterministic scalar per record from a key whose arity is a parameter.
            SELECT record_id,
                   string_agg(item_code || '=' || key_part, ' | ' ORDER BY ord) AS family_key
            FROM   parts GROUP BY record_id
        )
        SELECT g.family_key,
               count(*)::int                    AS instances,
               count(DISTINCT s.space_id)::int  AS space_count,
               array_agg(s.label ORDER BY s.label) AS members
        FROM   signature g JOIN scoped s ON s.record_id = g.record_id
        GROUP  BY g.family_key
        ORDER  BY instances DESC, g.family_key;
        """;

    private const string BoqSql = """
        WITH scoped AS (
            SELECT r."Id" AS record_id, r."SpaceId" AS space_id, r."ModuleId" AS module_id
            FROM   sites.site_records r
            JOIN   sites.sites s ON s."Id" = r."SiteId" AND s."IsActive"
            WHERE  r."SiteId" = @siteId AND r."IsActive"
        ),
        kind AS (
            -- DISTINCT ON, because a record may carry more than one Select value -- "kind" and
            -- "material", say -- and this CTE would otherwise emit one row per Select value. Every count
            -- and sum below is per row of `kind`, so a record with two Selects was counted twice and its
            -- quantity added to two different lines: 5 doors became a line of 5 doors AND a line of 5
            -- wood. The form's own field order decides which Select is the kind, so the tie breaks on
            -- the module link's sort order rather than on whatever the planner returned.
            SELECT DISTINCT ON (s.record_id)
                   s.record_id, s.space_id, s.module_id,
                   o."Code" AS kind_code, o."NameEn" AS name_en, o."NameAr" AS name_ar
            FROM   scoped s
            JOIN   sites.site_record_values v ON v."RecordId" = s.record_id
            JOIN   sites.site_items         i ON i."Id" = v."ItemId" AND i."DataType" = @selectDataType
            JOIN   sites.site_item_options  o ON o."ItemId" = v."ItemId" AND o."Id" = v."OptionId"
            JOIN   sites.site_module_items  l ON l."ModuleId" = s.module_id AND l."ItemId" = v."ItemId"
            ORDER  BY s.record_id, l."SortOrder", o."Code"
        ),
        measures AS (
            SELECT v."RecordId" AS record_id,
                   max(v."ValueNumber") FILTER (WHERE i."Code" = 'quantity')  AS qty,
                   max(v."ValueNumber") FILTER (WHERE i."Code" = 'width_cm')  AS width_cm,
                   max(v."ValueNumber") FILTER (WHERE i."Code" = 'height_cm') AS height_cm,
                   max(v."ValueNumber") FILTER (WHERE i."Code" = 'length_cm') AS length_cm
            FROM   sites.site_record_values v
            JOIN   sites.site_items i ON i."Id" = v."ItemId"
            WHERE  v."RecordId" IN (SELECT record_id FROM scoped)
              AND  i."Code" IN ('quantity','width_cm','height_cm','length_cm')
            GROUP  BY v."RecordId"
        )
        SELECT m."Code" AS module_code, k.kind_code, k.name_en, k.name_ar,
               count(*)::int                   AS occurrences,
               count(DISTINCT k.space_id)::int AS spaces,
               sum(coalesce(ms.qty, 1))        AS total_qty,
               -- Capture is in centimetres because the source form is; the report is in the trade's
               -- units. cm x cm = cm2, so /10000 for m2 and /100 for m. Paid once here, in a projection,
               -- rather than by the surveyor on every row.
               sum(coalesce(ms.qty,1) * ms.width_cm * ms.height_cm) / 10000.0 AS total_area_sqm,
               sum(coalesce(ms.qty,1) * ms.length_cm)               / 100.0   AS total_length_m
        FROM   kind k
        JOIN   sites.site_modules m ON m."Id" = k.module_id
        LEFT   JOIN measures ms ON ms.record_id = k.record_id
        GROUP  BY m."Code", m."SortOrder", k.kind_code, k.name_en, k.name_ar
        ORDER  BY m."SortOrder", k.kind_code;
        """;

    public async Task<IReadOnlyList<SiteFamilyResponse>> GetFamiliesAsync(
        Guid siteId, Guid moduleId, IReadOnlyList<Guid> groupByItemIds, CancellationToken ct = default)
    {
        if (groupByItemIds.Count == 0)
        {
            return [];
        }

        await using NpgsqlCommand command = await CreateCommandAsync(FamiliesSql, ct);
        command.Parameters.AddWithValue("siteId", siteId);
        command.Parameters.AddWithValue("moduleId", moduleId);
        command.Parameters.Add(new NpgsqlParameter("groupByIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            Value = groupByItemIds.ToArray(),
        });

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);

        var results = new List<SiteFamilyResponse>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new SiteFamilyResponse(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetFieldValue<string[]>(3)));
        }

        return results;
    }

    public async Task<IReadOnlyList<SiteBoqLineResponse>> GetBoqAsync(
        Guid siteId, CancellationToken ct = default)
    {
        await using NpgsqlCommand command = await CreateCommandAsync(BoqSql, ct);
        command.Parameters.AddWithValue("siteId", siteId);

        // Bound from the enum rather than written as a literal 4: renumbering SiteItemDataType would
        // otherwise leave this statement silently selecting the wrong data type.
        command.Parameters.AddWithValue("selectDataType", (int)Sites.Domain.SiteItemDataType.Select);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);

        var results = new List<SiteBoqLineResponse>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new SiteBoqLineResponse(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                // NULL totals read back as null, never 0 -- "nothing measured" and "measured zero" are
                // different answers and the BOQ must not conflate them.
                await reader.IsDBNullAsync(6, ct) ? null : reader.GetDecimal(6),
                await reader.IsDBNullAsync(7, ct) ? null : reader.GetDecimal(7),
                await reader.IsDBNullAsync(8, ct) ? null : reader.GetDecimal(8)));
        }

        return results;
    }

    private async Task<NpgsqlCommand> CreateCommandAsync(string sql, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        return new NpgsqlCommand(sql, connection);
    }
}
