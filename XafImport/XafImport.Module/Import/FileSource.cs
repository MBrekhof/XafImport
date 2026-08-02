#nullable enable
using XafImport.Module.BusinessObjects.Import;

namespace XafImport.Module.Import
{
    // ISource for uploaded files: picks the parser by the definition's explicit FileFormat,
    // or by magic bytes when FileFormat is Auto. Parser registration order = detection order
    // (TXT is the catch-all and must be registered last).
    public sealed class FileSource : ISource
    {
        private readonly IReadOnlyList<IFormatParser> parsers;

        public FileSource(IEnumerable<IFormatParser> parsers) => this.parsers = parsers.ToList();

        public async Task<ISourceReader> OpenAsync(ImportDefinition definition, Stream? uploadedFile, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(uploadedFile, nameof(uploadedFile));

            // ponytail: buffer to memory for sniffing + reparse; stream from disk when uploads get big.
            var buffer = new MemoryStream();
            await uploadedFile.CopyToAsync(buffer, ct);
            buffer.Position = 0;

            IFormatParser parser;
            if (definition.FileFormat != FileFormat.Auto)
            {
                parser = ByFormat(definition.FileFormat);
            }
            else
            {
                Span<byte> head = stackalloc byte[512];
                var headLength = buffer.Read(head);
                buffer.Position = 0;
                var containerFormat = DetectContainerFormat(head[..headLength], buffer);
                parser = containerFormat is { } format
                    ? ByFormat(format)
                    : FindByMagicBytes(head[..headLength])
                        ?? throw new NotSupportedException("Could not detect the file format from content.");
            }
            return parser.Open(buffer);
        }

        private IFormatParser ByFormat(FileFormat format)
            => parsers.FirstOrDefault(p => p.Format == format)
                ?? throw new NotSupportedException($"No parser implemented for format {format}.");

        // PK zips are both xlsx and docx — peek inside; OLE compound files (legacy xls/doc)
        // default to Xls (set FileFormat=Doc explicitly for legacy .doc — known POC ceiling).
        private static FileFormat? DetectContainerFormat(ReadOnlySpan<byte> head, MemoryStream buffer)
        {
            if (head.Length >= 4 && head[..2].SequenceEqual("PK"u8))
            {
                using var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
                var format = zip.Entries.Any(e => e.FullName.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)) ? FileFormat.Xls
                    : zip.Entries.Any(e => e.FullName.StartsWith("word/", StringComparison.OrdinalIgnoreCase)) ? FileFormat.Doc
                    : (FileFormat?)null;
                buffer.Position = 0;
                return format;
            }
            if (head.Length >= 4 && head[..4].SequenceEqual(new byte[] { 0xD0, 0xCF, 0x11, 0xE0 }))
            {
                return FileFormat.Xls;
            }
            return null;
        }

        private IFormatParser? FindByMagicBytes(ReadOnlySpan<byte> head)
        {
            foreach (var parser in parsers)
            {
                if (parser.CanParse(head, null))
                {
                    return parser;
                }
            }
            return null;
        }
    }
}
