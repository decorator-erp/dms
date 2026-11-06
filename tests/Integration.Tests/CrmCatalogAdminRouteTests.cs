using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>F4 (D4/D5): catalog admin. Update, active and delete for categories, items, attributes and options
/// (delete only when unused: 409 <c>crm.catalog.InUse</c>); the attribute's option list with or without inactive
/// entries; un-targeting a category from a space type; the tree filtered for a space type's picker (active
/// only).</summary>
[Collection(ApiCollection.Name)]
public sealed class CrmCatalogAdminRouteTests(ApiFixture fixture)
{
    [Fact]
    public async Task Catalog_update_active_delete_in_use_options_untarget_and_tree_filter()
    {
        string s = Guid.NewGuid().ToString("N")[..8];
        (_, string userId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"f4-{s}@example.com");
        HttpClient admin = CrmPortalTestSeeding.Client(fixture, userId, "crm.catalog.read", "crm.catalog.manage", "crm.space_types.read", "crm.space_types.manage");
        HttpClient reader = CrmPortalTestSeeding.Client(fixture, userId, "crm.catalog.read");
        const string c = "/api/crm/catalog";

        Guid spaceType = await IdAsync(await admin.PostAsJsonAsync("/api/crm/space-types", new { Code = $"ST{s}", NameEn = "Kitchen", NameAr = "مطبخ" }), "spaceTypeId");
        Guid category = await IdAsync(await admin.PostAsJsonAsync($"{c}/categories", new { Code = $"C{s}", NameEn = "Cabinets", NameAr = "خزائن", SortOrder = 1 }), "categoryId");
        Guid item = await IdAsync(await admin.PostAsJsonAsync($"{c}/items", new { CategoryId = category, Code = $"I{s}", NameEn = "Base unit", NameAr = "وحدة" }), "itemId");
        Guid attribute = await IdAsync(await admin.PostAsJsonAsync($"{c}/attributes", new { CategoryId = category, Code = $"A{s}", NameEn = "Finish", NameAr = "تشطيب", DataType = 5 }), "attributeId");
        Guid option = await IdAsync(await admin.PostAsJsonAsync($"{c}/attributes/{attribute}/options", new { Code = $"O{s}", NameEn = "Oak", NameAr = "بلوط" }), "optionId");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{c}/categories/{category}/space-types/{spaceType}", null)).StatusCode);

        // 403, 404, bad kind.
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PutAsJsonAsync($"{c}/categories/{category}", new { Code = $"C{s}", NameEn = "x", NameAr = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"{c}/items/{Guid.NewGuid()}", new { Code = "x", NameEn = "x", NameAr = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"{c}/widgets/{item}")).StatusCode);

        // Update each kind.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{c}/categories/{category}", new { Code = $"C{s}", NameEn = "Cabinetry", NameAr = "خزائن" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{c}/items/{item}", new { Code = $"I{s}", NameEn = "Base unit 60", NameAr = "وحدة" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{c}/attributes/{attribute}", new { Code = $"A{s}", NameEn = "Surface", NameAr = "سطح" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{c}/options/{option}", new { Code = $"O{s}", NameEn = "White oak", NameAr = "بلوط" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"{c}/options/{option}", new { Code = "", NameEn = "", NameAr = "" })).StatusCode);
        Assert.Contains(await ArrayAsync(reader, $"{c}/tree"), x => x.GetProperty("id").GetGuid() == category && x.GetProperty("nameEn").GetString() == "Cabinetry");
        Assert.Equal("Base unit 60", (await ArrayAsync(reader, $"{c}/categories/{category}/items")).Single().GetProperty("nameEn").GetString());

        // Options list and active.
        Assert.Equal("White oak", (await ArrayAsync(reader, $"{c}/attributes/{attribute}/options")).Single().GetProperty("nameEn").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{c}/options/{option}/active", new { IsActive = false })).StatusCode);
        Assert.Empty(await ArrayAsync(reader, $"{c}/attributes/{attribute}/options"));
        Assert.Single(await ArrayAsync(reader, $"{c}/attributes/{attribute}/options?includeInactive=true"));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{c}/options/{option}/active", new { IsActive = true })).StatusCode);

        // Picker tree for the space type: targeted and active only; untarget removes it.
        Assert.Contains(await ArrayAsync(reader, $"{c}/tree?spaceTypeId={spaceType}"), x => x.GetProperty("id").GetGuid() == category);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{c}/categories/{category}/active", new { IsActive = false })).StatusCode);
        Assert.DoesNotContain(await ArrayAsync(reader, $"{c}/tree?spaceTypeId={spaceType}"), x => x.GetProperty("id").GetGuid() == category);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{c}/categories/{category}/active", new { IsActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{c}/categories/{category}/space-types/{spaceType}")).StatusCode);
        Assert.DoesNotContain(await ArrayAsync(reader, $"{c}/tree?spaceTypeId={spaceType}"), x => x.GetProperty("id").GetGuid() == category);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"{c}/categories/{category}/space-types/{spaceType}")).StatusCode);

        // Delete if unused: the option is referenced by the item's value -> 409; the category has children -> 409.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{c}/items/{item}/attribute-values",
            new { Values = new[] { new { AttributeId = attribute, OptionId = option } } })).StatusCode);
        HttpResponseMessage optionInUse = await admin.DeleteAsync($"{c}/options/{option}");
        Assert.Equal(HttpStatusCode.Conflict, optionInUse.StatusCode);
        Assert.Contains("crm.catalog.InUse", await optionInUse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"{c}/attributes/{attribute}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"{c}/categories/{category}")).StatusCode);

        // Remove from the bottom up: item (its own values go with it), option, attribute, category.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{c}/items/{item}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{c}/options/{option}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{c}/attributes/{attribute}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{c}/categories/{category}")).StatusCode);
        Assert.DoesNotContain(await ArrayAsync(reader, $"{c}/tree"), x => x.GetProperty("id").GetGuid() == category);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"{c}/categories/{category}")).StatusCode);
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
