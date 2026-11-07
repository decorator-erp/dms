using System.Security.Cryptography;

namespace Projects.Infrastructure.Persistence;

/// <summary>
/// B-P-M7: the approval id of an approval gate, derived from the gate id — a name-based GUID (RFC 9562 version 5
/// layout, SHA-256 instead of SHA-1, as <c>CrmApprovalIds</c> does: the bits make it a well-formed GUID, the hash
/// only has to be stable).
///
/// <para><b>Why deterministic.</b> The gate's approval is requested inside the command's Projects transaction, but it
/// commits in Approvals' own transaction first. The approval and the gate are therefore bound by an id either side
/// can compute: a request repeated for the same gate returns the approval already made (with its decision, when it
/// was decided in between), which the handler applies to the gate instead of opening a second request; and (R1-6) a
/// decision whose approval id is <see cref="For"/> its subject is known to be a Projects gate's, so when that gate
/// is not visible yet (its transaction still open) <c>ApprovalDecidedIntegrationEventHandler</c> fails and the outbox
/// retries it, while a decision on any other approval is not this module's to wait for.</para>
/// </summary>
internal static class GateApprovalIds
{
    /// <summary>Fixed forever: changing it would re-key every gate approval still in flight.</summary>
    private static readonly Guid Namespace = new("0b8e5f43-6a2c-4d71-8f19-3c5e7a9d2b64");

    public static Guid For(Guid gateId)
    {
        Span<byte> name = stackalloc byte[32];
        Namespace.TryWriteBytes(name[..16], bigEndian: true, out _);
        gateId.TryWriteBytes(name[16..], bigEndian: true, out _);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(name, hash);

        Span<byte> bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5 (name-based)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // variant 10xx (RFC 4122/9562)

        return new Guid(bytes, bigEndian: true);
    }
}
