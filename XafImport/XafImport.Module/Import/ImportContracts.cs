#nullable enable
using XafImport.Module.BusinessObjects.Import;

namespace XafImport.Module.Import
{
    public sealed record RecordColumn(string Name, Type ClrType, bool IsNullable = true);

    public sealed record RecordSchema(IReadOnlyList<RecordColumn> Columns);

    // One implementation per SourceType: File, SqlServer, Api.
    public interface ISource
    {
        Task<ISourceReader> OpenAsync(ImportDefinition definition, Stream? uploadedFile, CancellationToken ct);
    }

    public interface ISourceReader : IAsyncDisposable
    {
        RecordSchema Schema { get; }
        IAsyncEnumerable<IDictionary<string, object?>> ReadAsync(CancellationToken ct);
    }

    // File-source parsers (one per format); detection by magic bytes, never extension.
    public interface IFormatParser
    {
        FileFormat Format { get; }
        bool CanParse(ReadOnlySpan<byte> magicBytes, string? fileNameHint);
        ISourceReader Open(Stream stream);
    }

    public interface IStagingLoader
    {
        // Creates/extends stg_<definition> from the schema (add-only), truncates, returns table name.
        Task<string> EnsureTableAsync(string definitionName, RecordSchema schema, CancellationToken ct);

        // Bulk-loads records; per-record failures go to onRecordError and loading continues. Returns staged count.
        Task<int> LoadAsync(string tableName, RecordSchema schema, Guid runId,
            IAsyncEnumerable<IDictionary<string, object?>> records,
            Action<int, Exception> onRecordError, CancellationToken ct);
    }
}
