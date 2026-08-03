using BuildingBlocks;
using Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

internal sealed class UserPermissionOverrideConfiguration : IEntityTypeConfiguration<UserPermissionOverride>
{
    public void Configure(EntityTypeBuilder<UserPermissionOverride> builder)
    {
        builder.ToTable("user_permission_overrides");
        builder.ConfigureEntityConventions();

        builder.Property(o => o.UserId).HasMaxLength(450).IsRequired();
        builder.Property(o => o.Permission).HasMaxLength(200).IsRequired();
        builder.Property(o => o.GrantedById).HasMaxLength(450).IsRequired();
        builder.Property(o => o.Reason).HasMaxLength(500);

        // Deny-wins precedence (see EffectivePermissionService, G1-04) is a data invariant enforced by
        // this index, not by code: a user can hold at most one override row per permission.
        builder.HasIndex(o => new { o.UserId, o.Permission }).IsUnique();
        builder.HasIndex(o => o.ExpiresAt);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
