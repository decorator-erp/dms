using Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("users");

        builder.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.UserType).HasConversion<int>();

        builder.OwnsMany(u => u.RefreshTokens, refreshToken =>
        {
            refreshToken.ToTable("refresh_tokens");
            refreshToken.WithOwner().HasForeignKey("user_id");
            refreshToken.Property<int>("id");
            refreshToken.HasKey("id");

            refreshToken.Property(t => t.Token).HasMaxLength(256).IsRequired();
            refreshToken.Property(t => t.CreatedOn).IsRequired();
            refreshToken.Property(t => t.ExpiresOn).IsRequired();
            refreshToken.Property(t => t.RevokedOn);

            refreshToken.HasIndex(t => t.Token).IsUnique();
        });
    }
}
