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
                parser = parsers.FirstOrDefault(p => p.Format == definition.FileFormat)
                    ?? throw new NotSupportedException($"No parser implemented for format {definition.FileFormat}.");
            }
            else
            {
                Span<byte> head = stackalloc byte[512];
                var headLength = buffer.Read(head);
                buffer.Position = 0;
                parser = FindByMagicBytes(head[..headLength])
                    ?? throw new NotSupportedException("Could not detect the file format from content.");
            }
            return parser.Open(buffer);
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
