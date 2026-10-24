using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Crm.Infrastructure.Tests;

/// <summary>L1: the lead row carries Postgres' <c>xmin</c> as its optimistic-concurrency token, the same
/// mapping as the brief head. Model only — no Docker.</summary>
public class CrmLeadConcurrencyModelTests
{
    [Fact]
    public void CrmLead_maps_xmin_as_its_concurrency_token()
    {
        using var db = new CrmDbContext(new DbContextOptionsBuilder<CrmDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options);

        IProperty? xmin = db.Model.FindEntityType(typeof(CrmLead))!.FindProperty("xmin");

        Assert.NotNull(xmin);
        Assert.True(xmin!.IsConcurrencyToken);
        Assert.Equal("xmin", xmin.GetColumnName());
        Assert.Equal(typeof(uint), xmin.ClrType);
    }
}
