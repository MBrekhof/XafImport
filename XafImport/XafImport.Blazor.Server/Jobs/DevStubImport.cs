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
        };

        private readonly ImportService importService;
        private readonly IObjectSpaceFactory objectSpaceFactory;
        private readonly FileSource fileSource;

        public DevStubImportHandler(ImportService importService, IObjectSpaceFactory objectSpaceFactory, FileSource fileSource)
        {
            this.importService = importService;
            this.objectSpaceFactory = objectSpaceFactory;
            this.fileSource = fileSource;
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
                    definition.SourceType = SourceType.File;
                    definition.FileFormat = FileFormat.Auto;
                    os.CommitChanges();
                }
                definitionId = definition.ID;
            }
            if (command.Format == null)
            {
                await importService.RunAsync(definitionId, new StubSource(), null, "dev stub (3 records)", ct);
            }
            else
            {
                using var payload = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(SamplePayloads[command.Format]));
                await importService.RunAsync(definitionId, fileSource, payload, $"dev sample ({command.Format}, auto-detect)", ct);
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
