using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel;

namespace BuildingBlocks;

/// <summary>
/// EF conventions every <see cref="Entity"/>-derived type needs, regardless of module: <c>Id</c> is
/// assigned in the constructor (<c>Guid.CreateVersion7()</c>), never DB-generated, and
/// <c>DomainEvents</c> is in-memory bookkeeping with no column of its own. Centralized here so every
/// module's entity configurations don't repeat the same two lines.
/// </summary>
public static class EntityModelConfigurationExtensions
{
    public static void ConfigureEntityConventions<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : Entity
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Ignore(e => e.DomainEvents);
    }
}
