using Approvals.Domain;
using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Approvals.Infrastructure.Persistence.Configurations;

internal sealed class ApprovalConfiguration : IEntityTypeConfiguration<Approval>
{
    public void Configure(EntityTypeBuilder<Approval> builder)
    {
        builder.ToTable("approvals");
        builder.ConfigureEntityConventions();

        builder.Property(a => a.SubjectType).HasConversion<int>();
        builder.Property(a => a.Type).HasConversion<int>();
        builder.Property(a => a.Decision).HasConversion<int?>();

        // RequestedByUserId is a bare string FK to identity.users(id) - no navigation, no HasOne (the two
        // entities are in different EF models). The actual FK CONSTRAINT is added via raw SQL in the
        // migration (see docs/cross-schema-foreign-keys.md and G4-03's PROGRESS.md entry) - EF cannot
        // emit it across models it doesn't know are related. 450 matches identity.users' own Id column
        // length (ASP.NET Identity's default), same convention HR's AppUserId FK uses.
        builder.Property(a => a.RequestedByUserId).HasMaxLength(450).IsRequired();
        builder.HasIndex(a => a.RequestedByUserId);

        builder.Property(a => a.DecidedByUserId).HasMaxLength(450);
        builder.Property(a => a.Comment).HasMaxLength(2000);

        // Every undecided approval on a subject is looked up on decide/request (OpenApprovalExists) and
        // on the my-pending-approvals inbox query - both filter on (SubjectType, SubjectId) or on
        // (Decision, RequestedByUserId)/(Decision, approver membership), so this composite index is the
        // hot path for both.
        builder.HasIndex(a => new { a.SubjectType, a.SubjectId });

        // S3: a bare cross-module id, no FK (the subject's project, as the requesting module stated it).
        builder.Property(a => a.ProjectId);

        // Approvers has no FK of its own - each entry's ApproverUserId is an unvalidated identity user id
        // (same "bare cross-module reference, no constraint" pattern as Project.ClientId elsewhere in
        // this plan) - only RequestedByUserId gets a real FK. Owned collection table, same shape as
        // Identity.Domain.ApplicationUser.RefreshTokens (ApplicationUserConfiguration.cs) - not a JSON
        // column, since that's the one owned-collection pattern already established in this codebase.
        builder.OwnsMany(a => a.Approvers, approver =>
        {
            approver.ToTable("approval_approvers");
            approver.WithOwner().HasForeignKey("approval_id");
            approver.Property<int>("id");
            approver.HasKey("id");

            approver.Property(a => a.Kind).HasConversion<int>();
            approver.Property(a => a.ApproverUserId).HasMaxLength(450).IsRequired();
        });

        // Computed from Decision (see Approval.State's own remarks) - not a column.
        builder.Ignore(a => a.State);
    }
}
