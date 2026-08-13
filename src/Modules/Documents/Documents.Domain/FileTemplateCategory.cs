namespace Documents.Domain;

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/FileTemplateCategory.cs</c>, numeric values
/// included — stored via <c>HasConversion&lt;int&gt;()</c> (G7-03), so the numbering is a wire/DB
/// contract, not just a display order.</summary>
internal enum FileTemplateCategory
{
    Other = 0,
    Cad = 1,
    Document = 2,
    Spreadsheet = 3,
    Image = 4,
    Archive = 5,
}
