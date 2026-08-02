#nullable enable
using DevExpress.Pdf;
using XafImport.Module.BusinessObjects.Import;

namespace XafImport.Module.Import.Formats
{
    // PDF via the DevExpress PDF Document API (user decision 2026-08-02).
    // POC scope: text extraction — one record per page (Page, Text). Table extraction is out of scope.
    public sealed class PdfFormatParser : IFormatParser
    {
        public FileFormat Format => FileFormat.Pdf;

        public bool CanParse(ReadOnlySpan<byte> magicBytes, string? fileNameHint)
            => magicBytes.Length >= 4 && magicBytes[..4].SequenceEqual("%PDF"u8);

        public ISourceReader Open(Stream stream)
        {
            using var processor = new PdfDocumentProcessor();
            processor.LoadDocument(stream);
            var records = new List<IDictionary<string, object?>>();
            var pageCount = processor.Document.Pages.Count;
            for (var page = 1; page <= pageCount; page++)
            {
                records.Add(new Dictionary<string, object?>
                {
                    ["Page"] = page,
                    ["Text"] = processor.GetPageText(page),
                });
            }
            var schema = new RecordSchema(new List<RecordColumn>
            {
                new("Page", typeof(int)),
                new("Text", typeof(string)),
            });
            return new BufferedReader(schema, records);
        }
    }
}
