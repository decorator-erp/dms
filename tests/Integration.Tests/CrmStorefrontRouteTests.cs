using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>F6 (D3): the space storefront. The office picks catalog items and options for a space (active item,
/// category targeted to the space's type, each option of its attribute, quantity &gt; 0); the brief-space entry
/// snapshots the picks when sent; the client sees them read-only in the portal brief and approves them with the
/// entry; a pick changed after approval goes out as a new round whose snapshot differs from the approved one.
/// The catalog item and option in use can't be hard-deleted, nor can the space.</summary>
[Collection(ApiCollection.Name)]
public sealed class CrmStorefrontRouteTests(ApiFixture fixture)
{
    [Fact]
    public async Task Picks_rules_snapshot_portal_view_and_new_round_after_approval()
    {
        PortalWorld w = await CrmPortalTestSeeding.CreateAsync(fixture, "f6");
        string s = Guid.NewGuid().ToString("N")[..8];
        HttpClient o = w.Office;
        const string c = "/api/crm/catalog";
        Guid kitchen = await IdAsync(await o.PostAsJsonAsync("/api/crm/space-types", new { Code = $"K{s}", NameEn = "Kitchen", NameAr = "مطبخ" }), "spaceTypeId");
        Guid bath = await IdAsync(await o.PostAsJsonAsync("/api/crm/space-types", new { Code = $"B{s}", NameEn = "Bath", NameAr = "حمام" }), "spaceTypeId");
        Guid category = await IdAsync(await o.PostAsJsonAsync($"{c}/categories", new { Code = $"C{s}", NameEn = "Cabinets", NameAr = "خزائن" }), "categoryId");
        Assert.Equal(HttpStatusCode.NoContent, (await o.PostAsync($"{c}/categories/{category}/space-types/{kitchen}", null)).StatusCode);
        Guid item = await IdAsync(await o.PostAsJsonAsync($"{c}/items", new { CategoryId = category, Code = $"I{s}", NameEn = "Base unit", NameAr = "وحدة" }), "itemId");
        Guid finish = await IdAsync(await o.PostAsJsonAsync($"{c}/attributes", new { CategoryId = category, Code = $"F{s}", NameEn = "Finish", NameAr = "تشطيب", DataType = 5 }), "attributeId");
        Guid oak = await IdAsync(await o.PostAsJsonAsync($"{c}/attributes/{finish}/options", new { Code = $"OAK{s}", NameEn = "Oak", NameAr = "بلوط" }), "optionId");
        Guid walnut = await IdAsync(await o.PostAsJsonAsync($"{c}/attributes/{finish}/options", new { Code = $"WAL{s}", NameEn = "Walnut", NameAr = "جوز" }), "optionId");
        Guid other = await IdAsync(await o.PostAsJsonAsync($"{c}/attributes", new { CategoryId = category, Code = $"H{s}", NameEn = "Handle", NameAr = "مقبض", DataType = 5 }), "attributeId");

        Guid kitchenSpace = await IdAsync(await o.PostAsJsonAsync($"/api/crm/projects/{w.ProjectId}/spaces", new { SpaceTypeId = kitchen, Name = "Main kitchen" }), "spaceId");
        Guid bathSpace = await IdAsync(await o.PostAsJsonAsync($"/api/crm/projects/{w.ProjectId}/spaces", new { SpaceTypeId = bath, Name = "Guest bath" }), "spaceId");
        string picks = $"/api/crm/spaces/{kitchenSpace}/selections";

        // Rules.
        Assert.Equal(HttpStatusCode.Forbidden, (await CrmPortalTestSeeding.Client(fixture, w.OfficeUserId, "crm.spaces.read")
            .PostAsJsonAsync(picks, new { CatalogItemId = item, Quantity = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.PostAsJsonAsync(picks, new { CatalogItemId = item, Quantity = 0 })).StatusCode);
        HttpResponseMessage untargeted = await o.PostAsJsonAsync($"/api/crm/spaces/{bathSpace}/selections", new { CatalogItemId = item, Quantity = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, untargeted.StatusCode);
        Assert.Contains("crm.storefront.CategoryNotTargeted", await untargeted.Content.ReadAsStringAsync());
        HttpResponseMessage wrongOption = await o.PostAsJsonAsync(picks, new
        {
            CatalogItemId = item,
            Quantity = 1,
            Options = new[] { new { AttributeId = other, OptionId = oak } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrongOption.StatusCode);
        Assert.Contains("crm.storefront.OptionNotOfAttribute", await wrongOption.Content.ReadAsStringAsync());

        // Add, list, update, reorder, remove.
        Guid pick = await IdAsync(await o.PostAsJsonAsync(picks, new
        {
            CatalogItemId = item,
            Quantity = 2,
            Notes = "Left wall",
            Options = new[] { new { AttributeId = finish, OptionId = oak } },
        }), "selectionId");
        Guid spare = await IdAsync(await o.PostAsJsonAsync(picks, new { CatalogItemId = item, Quantity = 1 }), "selectionId");
        JsonElement[] rows = await ArrayAsync(o, picks);
        Assert.Equal(2, rows.Length);
        JsonElement first = rows.Single(r => r.GetProperty("id").GetGuid() == pick);
        Assert.Equal(2m, first.GetProperty("quantity").GetDecimal());
        Assert.Equal("Base unit", first.GetProperty("itemNameEn").GetString());
        Assert.Equal(oak, Assert.Single(first.GetProperty("options").EnumerateArray()).GetProperty("optionId").GetGuid());
        Assert.Equal(HttpStatusCode.NoContent, (await o.PutAsJsonAsync($"{picks}/order", new { SelectionIds = new[] { spare, pick } })).StatusCode);
        Assert.Equal(spare, (await ArrayAsync(o, picks))[0].GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NoContent, (await o.DeleteAsync($"{picks}/{spare}")).StatusCode);

        // In use: the item, the option and the space can't be hard-deleted.
        Assert.Equal(HttpStatusCode.Conflict, (await o.DeleteAsync($"{c}/items/{item}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await o.DeleteAsync($"{c}/options/{oak}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await o.DeleteAsync($"/api/crm/spaces/{kitchenSpace}")).StatusCode);

        // The entry snapshots the picks when sent; the client sees them read-only and approves.
        Guid briefId = await IdAsync(await o.PostAsJsonAsync($"/api/crm/projects/{w.ProjectId}/brief",
            new { ProjectStageId = w.StageId, Data = new { UnitType = 1, UnitCondition = 1, Address = "F6" } }), "briefId");
        Guid entry = await IdAsync(await o.PostAsJsonAsync($"/api/crm/briefs/{briefId}/spaces", new { SpaceId = kitchenSpace, CountOrPieces = 1, Requirements = "Shaker" }), "briefSpaceId");
        Assert.Equal(HttpStatusCode.NoContent, (await o.PutAsync($"/api/crm/brief-spaces/{entry}/send", null)).StatusCode);

        HttpClient portal = CrmPortalTestSeeding.Client(fixture, w.ClientUserId,
            "crm.portal", "crm.briefs.view_own", "crm.approvals.view_own", "crm.approvals.decide_own");
        JsonElement portalEntry = await PortalEntryAsync(portal, w, entry);
        JsonElement snapPick = Assert.Single(portalEntry.GetProperty("selections").EnumerateArray());
        Assert.Equal(2m, snapPick.GetProperty("quantity").GetDecimal());
        Assert.Equal("Oak", Assert.Single(snapPick.GetProperty("options").EnumerateArray()).GetProperty("optionNameEn").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await portal.PostAsJsonAsync(picks, new { CatalogItemId = item, Quantity = 1 })).StatusCode);

        JsonElement approvals = await portal.GetFromJsonAsync<JsonElement>("/api/crm/portal/approvals?status=pending&pageNumber=1&pageSize=50");
        Guid approvalId = approvals.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("subjectId").GetGuid() == entry).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await portal.PutAsJsonAsync($"/api/crm/portal/approvals/{approvalId}/decide", new { Decision = 1 })).StatusCode);
        await CrmPortalTestSeeding.DrainAsync(fixture);

        // A pick changed after approval: the approved snapshot stays, a new round carries the change.
        Assert.Equal(HttpStatusCode.NoContent, (await o.PutAsJsonAsync($"{picks}/{pick}", new
        {
            Quantity = 3,
            Options = new[] { new { AttributeId = finish, OptionId = walnut } },
        })).StatusCode);
        Guid next = await IdAsync(await o.PostAsJsonAsync($"/api/crm/brief-spaces/{entry}/supersede", new { SpaceId = kitchenSpace, CountOrPieces = 1, Requirements = "Shaker" }), "briefSpaceId");
        Assert.Equal(HttpStatusCode.NoContent, (await o.PutAsync($"/api/crm/brief-spaces/{next}/send", null)).StatusCode);

        JsonElement[] entries = await ArrayAsync(o, $"/api/crm/briefs/{briefId}/spaces");
        JsonElement approved = entries.Single(e => e.GetProperty("id").GetGuid() == entry);
        JsonElement round2 = entries.Single(e => e.GetProperty("id").GetGuid() == next);
        Assert.Equal(2m, approved.GetProperty("selections")[0].GetProperty("quantity").GetDecimal());
        Assert.Equal("Oak", approved.GetProperty("selections")[0].GetProperty("options")[0].GetProperty("optionNameEn").GetString());
        Assert.Equal(3m, round2.GetProperty("selections")[0].GetProperty("quantity").GetDecimal());
        Assert.Equal("Walnut", round2.GetProperty("selections")[0].GetProperty("options")[0].GetProperty("optionNameEn").GetString());
        Assert.Equal(2, round2.GetProperty("approvalStatus").GetInt32()); // PendingApproval: a new round
    }

    private static async Task<JsonElement> PortalEntryAsync(HttpClient portal, PortalWorld w, Guid entryId)
    {
        JsonElement view = await portal.GetFromJsonAsync<JsonElement>($"/api/crm/portal/projects/{w.ProjectId}/brief?stageId={w.StageId}");
        return view.GetProperty("spaces").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == entryId);
    }

    private static async Task<Guid> IdAsync(HttpResponseMessage response, string name)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty(name).GetGuid();
    }

    private static async Task<JsonElement[]> ArrayAsync(HttpClient client, string url)
    {
        HttpResponseMessage response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return [.. (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()];
    }
}
