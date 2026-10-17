using Microsoft.AspNetCore.Identity;
using SharedKernel;

namespace Identity.Domain;

/// <summary>Ported from <c>../backend/Domain/Entities/ApplicationRoles.cs</c> (backend file name did
/// not match the class name; this one does).</summary>
internal sealed class ApplicationRole : IdentityRole<string>, IAuditable
{
    private ApplicationRole() { }

    public static ApplicationRole Create(string id, string name, string? description = null) =>
        new()
        {
            Id = id,
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            Description = description,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        };
    
    public string? Description { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; private set; }

    public void Rename(string name)
    {
        Name = name;
        NormalizedName = name.ToUpperInvariant();
    }

    public void UpdateDescription(string? description) => Description = description;

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
