namespace SharedKernel;

/// <summary>
/// Marks a property whose value must never be written to the audit trail
/// (e.g. secrets, hashes, tokens). The audit serializer redacts these.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class AuditIgnoreAttribute : Attribute;
