#nullable enable
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DevExpress.ExpressApp;
using DevExpress.Persistent.BaseImpl.EF;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using XafImport.Module.BusinessObjects.Import;

namespace XafImport.Module.Import
{
    // EXP-001: export = SqlQuery extract (app DB by default) -> mapping transform -> file on the run.
    // Reuses the import run/log/notification lifecycle; the result is a downloadable FileData.
    // ponytail: records buffered in memory; SQL/API export TARGETS and business-object criteria
    // extraction wait until practice demands them.
    public sealed class ExportService
    {
        private readonly IObjectSpaceFactory objectSpaceFactory;
        private readonly SqlServerSource sqlServerSource;
        private readonly IConfiguration configuration;
        private readonly ILogger<ExportService> logger;

        public ExportService(IObjectSpaceFactory objectSpaceFactory, SqlServerSource sqlServerSource,
            IConfiguration configuration, ILogger<ExportService> logger)
        {
            this.objectSpaceFactory = objectSpaceFactory;
            this.sqlServerSource = sqlServerSource;
            this.configuration = configuration;
            this.logger = logger;
        }

        public async Task<Guid> RunAsync(Guid definitionId, CancellationToken ct = default)
        {
            using var os = objectSpaceFactory.CreateObjectSpace<ImportRun>();
            var definition = os.GetObjectByKey<ImportDefinition>(definitionId)
                ?? throw new ArgumentException($"ImportDefinition {definitionId} not found.");
            if (string.IsNullOrWhiteSpace(definition.SqlQuery))
            {
                throw new InvalidOperationException("Export definitions need SqlQuery as the data source.");
            }
            // Empty connection string = export from the app's own database (incl. staging tables).
            if (string.IsNullOrWhiteSpace(definition.SqlConnectionString))
            {
                definition.SqlConnectionString = configuration.GetConnectionString("ConnectionString");
            }
            var format = definition.FileFormat == FileFormat.Auto ? FileFormat.Json : definition.FileFormat;

            var run = os.CreateObject<ImportRun>();
            run.Definition = definition;
            run.SourceDescription = $"export ({format})";
            run.Started = DateTime.Now;
            run.Status = ImportRunStatus.Running;
            os.CommitChanges();

            try
            {
                ImportService.Log(os, run, ImportLogLevel.Info, "Extract: running export query");
                await using var reader = await sqlServerSource.OpenAsync(definition, null, ct);
                var mapping = MappingTransformer.FromJson(definition.ColumnMappingJson);
                var schema = mapping.Apply(reader.Schema);
                var records = new List<IDictionary<string, object?>>();
                await foreach (var record in reader.ReadAsync(ct))
                {
                    records.Add(mapping.Apply(record));
                }

                var (bytes, extension) = Serialize(records, schema, format);
                var file = os.CreateObject<FileData>();
                file.LoadFromStream($"{SqlServerStagingLoader.Sanitize(definition.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}",
                    new MemoryStream(bytes));
                run.ResultFile = file;
                run.RecordsRead = records.Count;
                run.RecordsStaged = records.Count;
                run.Status = ImportRunStatus.Succeeded;
                ImportService.Log(os, run, ImportLogLevel.Info,
                    $"Done: {records.Count} records exported to {file.FileName} ({bytes.Length} bytes)");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Export run for definition {Definition} failed", definition.Name);
                run.Status = ImportRunStatus.Failed;
                ImportService.Log(os, run, ImportLogLevel.Error, ex.ToString());
            }
            finally
            {
                run.Finished = DateTime.Now;
                ImportService.Notify(os, run, definition, "Export");
                os.CommitChanges();
            }
            return run.ID;
        }

        private static (byte[] Bytes, string Extension) Serialize(
            IReadOnlyList<IDictionary<string, object?>> records, RecordSchema schema, FileFormat format) => format switch
        {
            FileFormat.Json => (JsonSerializer.SerializeToUtf8Bytes(records, new JsonSerializerOptions { WriteIndented = true }), "json"),
            FileFormat.Xml => (SerializeXml(records, schema), "xml"),
            FileFormat.Txt => (SerializeCsv(records, schema), "csv"),
            FileFormat.Xls => (SerializeXlsx(records, schema), "xlsx"),
            _ => throw new NotSupportedException($"Export to {format} is not supported (pick Json, Xml, Txt/csv, or Xls)."),
        };

        private static byte[] SerializeXml(IReadOnlyList<IDictionary<string, object?>> records, RecordSchema schema)
        {
            var root = new XElement("rows",
                records.Select(r => new XElement("row",
                    schema.Columns
                        .Where(c => r.TryGetValue(c.Name, out var v) && v != null)
                        .Select(c => new XElement(SqlServerStagingLoader.Sanitize(c.Name), FormatValue(r[c.Name]))))));
            return Encoding.UTF8.GetBytes(new XDocument(root).ToString());
        }

        private static byte[] SerializeCsv(IReadOnlyList<IDictionary<string, object?>> records, RecordSchema schema)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", schema.Columns.Select(c => Quote(c.Name))));
            foreach (var record in records)
            {
                sb.AppendLine(string.Join(",", schema.Columns.Select(c =>
                    Quote(record.TryGetValue(c.Name, out var v) ? FormatValue(v) : string.Empty))));
            }
            return Encoding.UTF8.GetBytes(sb.ToString());

            static string Quote(string value) =>
                value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
        }

        private static byte[] SerializeXlsx(IReadOnlyList<IDictionary<string, object?>> records, RecordSchema schema)
        {
            using var workbook = new DevExpress.Spreadsheet.Workbook();
            var sheet = workbook.Worksheets[0];
            for (var c = 0; c < schema.Columns.Count; c++)
            {
                sheet[0, c].Value = schema.Columns[c].Name;
            }
            for (var r = 0; r < records.Count; r++)
            {
                for (var c = 0; c < schema.Columns.Count; c++)
                {
                    if (records[r].TryGetValue(schema.Columns[c].Name, out var v) && v != null)
                    {
                        sheet[r + 1, c].Value = DevExpress.Spreadsheet.CellValue.FromObject(v);
                    }
                }
            }
            using var ms = new MemoryStream();
            workbook.SaveDocument(ms, DevExpress.Spreadsheet.DocumentFormat.Xlsx);
            return ms.ToArray();
        }

        private static string FormatValue(object? value) => value switch
        {
            null => string.Empty,
            DateTime dt => dt.ToString("o"),
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }
}
