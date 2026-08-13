namespace Documents.Contracts;

/// <summary>Ported verbatim from <c>../backend/Domain/Constants/DefaultPermissions.cs</c>'s
/// Documents/FileTemplates entries — the actual strings already in use, not renamed or renumbered on
/// the way in. Folder endpoints reuse these same four Documents strings (create folder →
/// <see cref="Upload"/>, rename/move → <see cref="Update"/>, delete → <see cref="Delete"/>) — the
/// backend has no separate folder permission group, and this port doesn't invent one.</summary>
public static class DocumentsPermissions
{
    public const string Read = "documents.read";
    public const string Upload = "documents.upload";
    public const string Update = "documents.update";
    public const string Delete = "documents.delete";

    public const string FileTemplatesRead = "file_templates.read";
    public const string FileTemplatesManage = "file_templates.manage";
}
