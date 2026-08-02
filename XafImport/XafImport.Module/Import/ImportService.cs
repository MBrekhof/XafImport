#nullable enable
using System.Runtime.CompilerServices;
using System.Text.Json;
using DevExpress.ExpressApp;
using Microsoft.Extensions.Logging;
using XafImport.Module.BusinessObjects.Import;

namespace XafImport.Module.Import
{
    // Rename-only column mapping from ImportDefinition.ColumnMappingJson ({"Source":"Target",...}).
    // ponytail: type conversion/expressions when a real import needs them.
    public sealed class MappingTransformer
    {
        private readonly IReadOnlyDictionary<string, string> renames;

        private MappingTransformer(IReadOnlyDictionary<string, string> renames) => this.renames = renames;

        public static MappingTransformer FromJson(string? json) =>
            new(string.IsNullOrWhiteSpace(json)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(json)!);

        public RecordSchema Apply(RecordSchema schema) =>
            new(schema.Columns.Select(c => renames.TryGetValue(c.Name, out var target)
                ? c with { Name = target } : c).ToList());

        public IDictionary<string, object?> Apply(IDictionary<string, object?> record)
        {
            if (renames.Count == 0)
            {
                return record;
            }
            var result = new Dictionary<string, object?>(record.Count);
            foreach (var (key, value) in record)
            {
                result[renames.TryGetValue(key, out var target) ? target : key] = value;
            }
            return result;
        }
    }

    // The one pipeline both entry points call (manual UI action, Hangfire job):
    // extract (ISource) -> transform (mapping) -> load (staging). Owns the ImportRun lifecycle.
    public sealed class ImportService
    {
        private const int MaxRecordErrorLogs = 20;
        private readonly IObjectSpaceFactory objectSpaceFactory;
        private readonly IStagingLoader stagingLoader;
        private readonly ILogger<ImportService> logger;

        public ImportService(IObjectSpaceFactory objectSpaceFactory, IStagingLoader stagingLoader, ILogger<ImportService> logger)
        {
            this.objectSpaceFactory = objectSpaceFactory;
            this.stagingLoader = stagingLoader;
            this.logger = logger;
        }

        public async Task<Guid> RunAsync(Guid definitionId, ISource source, Stream? uploadedFile,
            string? sourceDescription, CancellationToken ct = default)
        {
            using var os = objectSpaceFactory.CreateObjectSpace<ImportRun>();
            var definition = os.GetObjectByKey<ImportDefinition>(definitionId)
                ?? throw new ArgumentException($"ImportDefinition {definitionId} not found.");
            var run = os.CreateObject<ImportRun>();
            run.Definition = definition;
            run.SourceDescription = sourceDescription;
            run.Started = DateTime.Now;
            run.Status = ImportRunStatus.Running;
            os.CommitChanges();

            int read = 0, failed = 0;
            try
            {
                Log(os, run, ImportLogLevel.Info, "Extract: opening source");
                await using var reader = await source.OpenAsync(definition, uploadedFile, ct);
                var mapping = MappingTransformer.FromJson(definition.ColumnMappingJson);
                var schema = mapping.Apply(reader.Schema);

                var tableName = await stagingLoader.EnsureTableAsync(definition.Name, schema, ct);
                run.StagingTableName = tableName;
                Log(os, run, ImportLogLevel.Info, $"Load: staging table {tableName} ready ({schema.Columns.Count} columns)");

                var onError = definition.OnError;
                var maxErrors = definition.MaxErrors;
                var staged = await stagingLoader.LoadAsync(tableName, schema, run.ID,
                    Transformed(reader.ReadAsync(ct), mapping, () => read++),
                    (rowNo, ex) =>
                    {
                        failed++;
                        if (failed <= MaxRecordErrorLogs)
                        {
                            Log(os, run, ImportLogLevel.Error, ex.Message, rowNo);
                        }
                        // CFG-001: error policy — abort on first failure, or when the cap is hit.
                        if (onError == ErrorPolicy.Abort)
                        {
                            throw new InvalidOperationException($"Run aborted: record {rowNo} failed and OnError is Abort. {ex.Message}");
                        }
                        if (maxErrors > 0 && failed >= maxErrors)
                        {
                            throw new InvalidOperationException($"Run aborted: {failed} record errors reached the MaxErrors limit ({maxErrors}).");
                        }
                    }, ct);
                if (failed > MaxRecordErrorLogs)
                {
                    Log(os, run, ImportLogLevel.Warning, $"{failed - MaxRecordErrorLogs} further record errors not logged individually");
                }

                run.RecordsRead = read;
                run.RecordsStaged = staged;
                run.RecordsFailed = failed;
                run.Status = failed == 0 ? ImportRunStatus.Succeeded : ImportRunStatus.SucceededWithErrors;
                Log(os, run, ImportLogLevel.Info, $"Done: {read} read, {staged} staged, {failed} failed");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Import run for definition {Definition} failed", definition.Name);
                run.RecordsRead = read;
                run.RecordsFailed = failed;
                run.Status = ImportRunStatus.Failed;
                Log(os, run, ImportLogLevel.Error, ex.ToString());
            }
            finally
            {
                run.Finished = DateTime.Now;
                if (definition.NotifyOnCompletion)
                {
                    // NOT-001: picked up by the XAF Notifications module on its next poll.
                    var notification = os.CreateObject<ImportNotification>();
                    notification.Run = run;
                    notification.AlarmTime = DateTime.Now;
                    notification.Message =
                        $"Import '{definition.Name}' {run.Status}: {run.RecordsRead} read, {run.RecordsStaged} staged, {run.RecordsFailed} failed";
                }
                os.CommitChanges();
            }
            return run.ID;
        }

        private static async IAsyncEnumerable<IDictionary<string, object?>> Transformed(
            IAsyncEnumerable<IDictionary<string, object?>> records, MappingTransformer mapping,
            Action onRead, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var record in records.WithCancellation(ct))
            {
                onRead();
                yield return mapping.Apply(record);
            }
        }

        private static void Log(IObjectSpace os, ImportRun run, ImportLogLevel level, string message, int? recordNumber = null)
        {
            var entry = os.CreateObject<ImportLogEntry>();
            entry.Run = run;
            entry.Timestamp = DateTime.Now;
            entry.Level = level;
            entry.Message = message;
            entry.RecordNumber = recordNumber;
            os.CommitChanges();
        }
    }
}
