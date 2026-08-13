using SharedKernel;

namespace Documents.Domain;

/// <summary>Raised by <see cref="ProjectDocument.Create"/> and <see cref="ProjectDocument.CreateRevision"/>.
/// Intra-module only — the cross-module signal is
/// <c>Documents.Contracts.DocumentUploadedIntegrationEvent</c>, enqueued directly by the upload command
/// handlers alongside this one (G7-07), not derived from it. Reserved for a future intra-module reaction
/// (none exists yet — no handler subscribes today, which is a valid no-op per every other module's own
/// "zero handlers" convention).</summary>
internal sealed record DocumentUploadedDomainEvent(Guid DocumentId) : IDomainEvent;
