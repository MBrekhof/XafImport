#nullable enable
using DevExpress.Spreadsheet;
using XafImport.Module.BusinessObjects.Import;

namespace XafImport.Module.Import.Formats
{
    // Excel via the DevExpress Spreadsheet Document API (user decision 2026-08-02).
    // First worksheet only; row 1 = headers, rows below = records.
    // ponytail: date cells arrive as Excel numerics unless typed by the API; revisit when a real sheet needs dates.
    public sealed class XlsFormatParser : IFormatParser
    {
        public FileFormat Format => FileFormat.Xls;

        public bool CanParse(ReadOnlySpan<byte> magicBytes, string? fileNameHint)
        {
            if (magicBytes.Length < 4)
            {
                return false;
            }
            // xlsx = zip (PK\x03\x04); legacy xls = OLE compound file. The compound signature is
            // shared with legacy .doc — FMT-005 takes over disambiguation when the DOC parser lands.
            return magicBytes[..4].SequenceEqual("PK"u8)
                || magicBytes[..4].SequenceEqual(new byte[] { 0xD0, 0xCF, 0x11, 0xE0 });
        }

        public ISourceReader Open(Stream stream)
        {
            using var workbook = new Workbook();
            workbook.LoadDocument(stream);
            var worksheet = workbook.Worksheets[0];
            var used = worksheet.GetUsedRange();

            var columns = new List<RecordColumn>();
            var headers = new List<string>();
            for (var c = 0; c < used.ColumnCount; c++)
            {
                var text = used[0, c].DisplayText?.Trim();
                headers.Add(string.IsNullOrEmpty(text) ? $"Column{c + 1}" : text!);
            }

            var records = new List<IDictionary<string, object?>>();
            var columnTypes = new Type?[headers.Count];
            for (var r = 1; r < used.RowCount; r++)
            {
                var record = new Dictionary<string, object?>();
                var isEmpty = true;
                for (var c = 0; c < headers.Count; c++)
                {
                    var value = ToClr(used[r, c].Value);
                    record[headers[c]] = value;
                    if (value != null)
                    {
                        isEmpty = false;
                        columnTypes[c] ??= value.GetType();
                    }
                }
                if (!isEmpty)
                {
                    records.Add(record);
                }
            }
            for (var c = 0; c < headers.Count; c++)
            {
                columns.Add(new RecordColumn(headers[c], columnTypes[c] ?? typeof(string)));
            }
            return new BufferedReader(new RecordSchema(columns), records);
        }

        private static object? ToClr(CellValue value)
        {
            if (value.IsNumeric)
            {
                return (decimal)value.NumericValue;
            }
            if (value.IsDateTime)
            {
                return value.DateTimeValue;
            }
            if (value.IsBoolean)
            {
                return value.BooleanValue;
            }
            if (value.IsText)
            {
                var text = value.TextValue;
                return string.IsNullOrEmpty(text) ? null : text;
            }
            return value.IsEmpty ? null : value.ToString();
        }
    }
}
