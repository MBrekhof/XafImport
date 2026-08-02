#nullable enable
using DevExpress.XtraRichEdit;
using XafImport.Module.BusinessObjects.Import;

namespace XafImport.Module.Import.Formats
{
    // Word via the DevExpress Word Processing Document API (user decision 2026-08-02).
    // POC scope: text extraction — one record per non-empty paragraph (Paragraph, Text).
    public sealed class DocFormatParser : IFormatParser
    {
        public FileFormat Format => FileFormat.Doc;

        // Never magic-picked: docx (PK zip) and legacy .doc (OLE compound) are dispatched by
        // FileSource container inspection or an explicit FileFormat on the definition.
        public bool CanParse(ReadOnlySpan<byte> magicBytes, string? fileNameHint) => false;

        public ISourceReader Open(Stream stream)
        {
            using var server = new RichEditDocumentServer();
            server.LoadDocument(stream);
            var document = server.Document;
            var records = new List<IDictionary<string, object?>>();
            var index = 0;
            foreach (var paragraph in document.Paragraphs)
            {
                index++;
                var text = document.GetText(paragraph.Range).Trim();
                if (text.Length > 0)
                {
                    records.Add(new Dictionary<string, object?>
                    {
                        ["Paragraph"] = index,
                        ["Text"] = text,
                    });
                }
            }
            var schema = new RecordSchema(new List<RecordColumn>
            {
                new("Paragraph", typeof(int)),
                new("Text", typeof(string)),
            });
            return new BufferedReader(schema, records);
        }
    }
}
