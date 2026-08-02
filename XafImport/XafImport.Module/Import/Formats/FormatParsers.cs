#nullable enable
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using XafImport.Module.BusinessObjects.Import;

namespace XafImport.Module.Import.Formats
{
    // ponytail: all three parsers buffer the whole payload — POC uploads are small.
    // Switch to streaming readers when a real import outgrows memory.

    internal sealed class BufferedReader : ISourceReader
    {
        private readonly IReadOnlyList<IDictionary<string, object?>> records;

        public BufferedReader(RecordSchema schema, IReadOnlyList<IDictionary<string, object?>> records)
        {
            Schema = schema;
            this.records = records;
        }

        public RecordSchema Schema { get; }

        public async IAsyncEnumerable<IDictionary<string, object?>> ReadAsync([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            foreach (var record in records)
            {
                ct.ThrowIfCancellationRequested();
                yield return record;
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // Root array of objects -> one record per element; root object -> a single record.
    public sealed class JsonFormatParser : IFormatParser
    {
        public FileFormat Format => FileFormat.Json;

        public bool CanParse(ReadOnlySpan<byte> magicBytes, string? fileNameHint)
        {
            var b = FirstContentByte(magicBytes);
            return b == (byte)'{' || b == (byte)'[';
        }

        public ISourceReader Open(Stream stream)
        {
            using var doc = JsonDocument.Parse(stream);
            var elements = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().ToList()
                : new List<JsonElement> { doc.RootElement };

            var records = new List<IDictionary<string, object?>>(elements.Count);
            var columns = new Dictionary<string, RecordColumn>();
            foreach (var element in elements.Where(e => e.ValueKind == JsonValueKind.Object))
            {
                var record = new Dictionary<string, object?>();
                foreach (var prop in element.EnumerateObject())
                {
                    var value = ToClr(prop.Value);
                    record[prop.Name] = value;
                    if (!columns.ContainsKey(prop.Name) && value != null)
                    {
                        columns[prop.Name] = new RecordColumn(prop.Name, value.GetType());
                    }
                }
                records.Add(record);
            }
            // Columns only ever seen as null: fall back to string.
            foreach (var name in records.SelectMany(r => r.Keys).Distinct().Where(n => !columns.ContainsKey(n)))
            {
                columns[name] = new RecordColumn(name, typeof(string));
            }
            return new BufferedReader(new RecordSchema(columns.Values.ToList()), records);
        }

        private static object? ToClr(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetDecimal(out var d) ? d : value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.GetRawText(), // nested objects/arrays land as raw JSON text
        };

        internal static byte FirstContentByte(ReadOnlySpan<byte> bytes)
        {
            var span = bytes;
            if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
            {
                span = span[3..]; // UTF-8 BOM
            }
            foreach (var b in span)
            {
                if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
                {
                    return b;
                }
            }
            return 0;
        }
    }

    // Root's child elements = records; each record's child elements = string columns.
    public sealed class XmlFormatParser : IFormatParser
    {
        public FileFormat Format => FileFormat.Xml;

        public bool CanParse(ReadOnlySpan<byte> magicBytes, string? fileNameHint)
            => JsonFormatParser.FirstContentByte(magicBytes) == (byte)'<';

        public ISourceReader Open(Stream stream)
        {
            var doc = XDocument.Load(stream);
            var rows = doc.Root?.Elements().ToList() ?? new List<XElement>();
            var records = new List<IDictionary<string, object?>>(rows.Count);
            var columnNames = new List<string>();
            foreach (var row in rows)
            {
                var record = new Dictionary<string, object?>();
                foreach (var cell in row.Elements())
                {
                    var name = cell.Name.LocalName;
                    record[name] = cell.Value;
                    if (!columnNames.Contains(name))
                    {
                        columnNames.Add(name);
                    }
                }
                records.Add(record);
            }
            var schema = new RecordSchema(columnNames.Select(n => new RecordColumn(n, typeof(string))).ToList());
            return new BufferedReader(schema, records);
        }
    }

    // One record per non-empty line, single "Line" column. Catch-all: register LAST.
    public sealed class TxtFormatParser : IFormatParser
    {
        public FileFormat Format => FileFormat.Txt;

        public bool CanParse(ReadOnlySpan<byte> magicBytes, string? fileNameHint) => true;

        public ISourceReader Open(Stream stream)
        {
            using var reader = new StreamReader(stream, leaveOpen: true);
            var records = new List<IDictionary<string, object?>>();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length > 0)
                {
                    records.Add(new Dictionary<string, object?> { ["Line"] = line });
                }
            }
            var schema = new RecordSchema(new List<RecordColumn> { new("Line", typeof(string)) });
            return new BufferedReader(schema, records);
        }
    }
}
