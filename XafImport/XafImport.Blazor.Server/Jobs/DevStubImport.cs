#nullable enable
using System.Runtime.CompilerServices;
using DevExpress.ExpressApp;
using XafImport.Module.BusinessObjects.Import;
using XafImport.Module.Import;
using XafImport.Module.Jobs;

namespace XafImport.Blazor.Server.Jobs
{
    // Dev-only smoke test: fixed records through the REAL execution path
    // (Hangfire queue -> XAF scope init -> ImportService -> staging table).
    // Format null = in-memory stub source; "json"/"xml"/"txt" = sample payload
    // through FileSource with FileFormat.Auto, exercising magic-byte detection.
    public sealed record DevStubImportCommand(string? Format = null);

    public sealed class DevStubImportHandler : IJobHandler<DevStubImportCommand>
    {
        private static readonly Dictionary<string, string> SamplePayloads = new()
        {
            ["json"] = """[{"Name":"a","Qty":1,"Active":true},{"Name":"b","Qty":2.5,"Active":false}]""",
            ["xml"] = "<rows><row><Name>a</Name><Qty>1</Qty></row><row><Name>b</Name><Qty>2</Qty></row></rows>",
            ["txt"] = "hello\nworld\nthird line",
            // Qty typed decimal from the first record; "oops" then fails conversion -> exercises CFG-001.
            ["jsonbad"] = """[{"Name":"ok1","Qty":1},{"Name":"broken","Qty":"oops"},{"Name":"ok2","Qty":3}]""",
        };

        private readonly ImportService importService;
        private readonly ExportService exportService;
        private readonly IObjectSpaceFactory objectSpaceFactory;
        private readonly FileSource fileSource;
        private readonly SqlServerSource sqlServerSource;
        private readonly IConfiguration configuration;

        public DevStubImportHandler(ImportService importService, ExportService exportService,
            IObjectSpaceFactory objectSpaceFactory,
            FileSource fileSource, SqlServerSource sqlServerSource, IConfiguration configuration)
        {
            this.importService = importService;
            this.exportService = exportService;
            this.objectSpaceFactory = objectSpaceFactory;
            this.fileSource = fileSource;
            this.sqlServerSource = sqlServerSource;
            this.configuration = configuration;
        }

        private static MemoryStream BuildSampleXlsx()
        {
            using var workbook = new DevExpress.Spreadsheet.Workbook();
            var sheet = workbook.Worksheets[0];
            sheet["A1"].Value = "Name";
            sheet["B1"].Value = "Qty";
            sheet["C1"].Value = "Ok";
            sheet["A2"].Value = "row1";
            sheet["B2"].Value = 12.5;
            sheet["C2"].Value = true;
            sheet["A3"].Value = "row2";
            sheet["B3"].Value = 7;
            sheet["C3"].Value = false;
            var ms = new MemoryStream();
            workbook.SaveDocument(ms, DevExpress.Spreadsheet.DocumentFormat.Xlsx);
            ms.Position = 0;
            return ms;
        }

        private static MemoryStream BuildSampleDocx()
        {
            using var server = new DevExpress.XtraRichEdit.RichEditDocumentServer();
            server.Document.AppendText("First paragraph from the dev sample.");
            server.Document.Paragraphs.Append();
            server.Document.AppendText("Second paragraph with more text.");
            var ms = new MemoryStream();
            server.SaveDocument(ms, DevExpress.XtraRichEdit.DocumentFormat.OpenXml);
            ms.Position = 0;
            return ms;
        }

        private static MemoryStream BuildSamplePdf()
        {
            using var server = new DevExpress.XtraRichEdit.RichEditDocumentServer();
            server.Document.AppendText("PDF sample line one.");
            server.Document.Paragraphs.Append();
            server.Document.AppendText("PDF sample line two.");
            var ms = new MemoryStream();
            server.ExportToPdf(ms);
            ms.Position = 0;
            return ms;
        }

        public async Task ExecuteAsync(DevStubImportCommand command, CancellationToken ct = default)
        {
            var definitionName = command.Format == null ? "DevTest" : "DevTest_" + command.Format;
            Guid definitionId;
            using (var os = objectSpaceFactory.CreateObjectSpace<ImportDefinition>())
            {
                var definition = os.FirstOrDefault<ImportDefinition>(d => d.Name == definitionName);
                if (definition == null)
                {
                    definition = os.CreateObject<ImportDefinition>();
                    definition.Name = definitionName;
                    definition.SourceType = command.Format == "sql" ? SourceType.SqlServer : SourceType.File;
                    definition.FileFormat = FileFormat.Auto;
                }
                if (command.Format == "sql")
                {
                    definition.SqlConnectionString = configuration.GetConnectionString("ConnectionString");
                    definition.SqlQuery = "SELECT name AS TableName, object_id AS ObjectId, create_date AS CreatedOn FROM sys.tables";
                }
                if (command.Format == "export")
                {
                    definition.Direction = TransferDirection.Export;
                    definition.FileFormat = FileFormat.Txt; // csv
                    definition.SqlQuery = "SELECT Name, Qty, Active FROM stg_DevTest_json";
                }
                os.CommitChanges();
                definitionId = definition.ID;
            }
            switch (command.Format)
            {
                case null:
                    await importService.RunAsync(definitionId, new StubSource(), null, "dev stub (3 records)", ct);
                    break;
                case "sql":
                    await importService.RunAsync(definitionId, sqlServerSource, null, "dev sql (sys.tables)", ct);
                    break;
                case "export":
                    await exportService.RunAsync(definitionId, ct);
                    break;
                case "xlsx":
                    using (var xlsx = BuildSampleXlsx())
                    {
                        await importService.RunAsync(definitionId, fileSource, xlsx, "dev sample (xlsx, auto-detect)", ct);
                    }
                    break;
                case "docx":
                    using (var docx = BuildSampleDocx())
                    {
                        await importService.RunAsync(definitionId, fileSource, docx, "dev sample (docx, auto-detect)", ct);
                    }
                    break;
                case "pdf":
                    using (var pdf = BuildSamplePdf())
                    {
                        await importService.RunAsync(definitionId, fileSource, pdf, "dev sample (pdf, auto-detect)", ct);
                    }
                    break;
                default:
                    using (var payload = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(SamplePayloads[command.Format])))
                    {
                        await importService.RunAsync(definitionId, fileSource, payload, $"dev sample ({command.Format}, auto-detect)", ct);
                    }
                    break;
            }
        }
    }

    internal sealed class StubSource : ISource
    {
        public Task<ISourceReader> OpenAsync(ImportDefinition definition, Stream? uploadedFile, CancellationToken ct)
            => Task.FromResult<ISourceReader>(new StubReader());

        private sealed class StubReader : ISourceReader
        {
            public RecordSchema Schema { get; } = new(new List<RecordColumn>
            {
                new("Name", typeof(string)),
                new("Amount", typeof(decimal)),
                new("When", typeof(DateTime)),
            });

            public async IAsyncEnumerable<IDictionary<string, object?>> ReadAsync([EnumeratorCancellation] CancellationToken ct)
            {
                await Task.CompletedTask;
                yield return new Dictionary<string, object?> { ["Name"] = "alpha", ["Amount"] = 12.5m, ["When"] = new DateTime(2026, 1, 1) };
                yield return new Dictionary<string, object?> { ["Name"] = "beta", ["Amount"] = 99.99m, ["When"] = new DateTime(2026, 2, 2) };
                yield return new Dictionary<string, object?> { ["Name"] = "gamma", ["Amount"] = 0m, ["When"] = new DateTime(2026, 3, 3) };
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
