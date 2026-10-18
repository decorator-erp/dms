using System.Security.Cryptography;
using Approvals.Contracts;

namespace Crm.Application.Features.Briefs;

/// <summary>
/// Deterministic approval ids for CRM's client-review rounds: one id per (subject type, subject id,
/// round), a name-based GUID (RFC 9562 version 5 layout, SHA-256 instead of SHA-1 — the bits are what
/// make it a well-formed GUID, the hash only has to be stable).
///
/// <para><b>Why deterministic.</b> The send handlers request the approval inside the command
/// pipeline's CRM transaction, but the approval commits in Approvals' own transaction first. If CRM's
/// commit then fails, the round number rolls back with it — so the retry computes the SAME round, hence
/// the same id, and <c>IApprovalRequester.RequestAsync</c> returns the already-committed approval (and
/// its decision, if it was decided in between) instead of tripping <c>OpenApprovalExists</c>.</para>
/// </summary>
internal static class CrmApprovalIds
{
    /// <summary>Fixed forever: changing it would re-key every round still in flight.</summary>
    private static readonly Guid Namespace = new("6f0c5c8e-3b1d-4c27-9a4e-2d8b7f51c0a3");

    public static Guid For(ApprovalSubjectType subjectType, Guid subjectId, int round)
    {
        Span<byte> name = stackalloc byte[16 + 4 + 16 + 4];
        Namespace.TryWriteBytes(name[..16], bigEndian: true, out _);
        BitConverter.TryWriteBytes(name.Slice(16, 4), (int)subjectType);
        subjectId.TryWriteBytes(name.Slice(20, 16), bigEndian: true, out _);
        BitConverter.TryWriteBytes(name.Slice(36, 4), round);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(name, hash);

        Span<byte> bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5 (name-based)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // variant 10xx (RFC 4122/9562)

        return new Guid(bytes, bigEndian: true);
    }
}
