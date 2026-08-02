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
    public sealed record DevStubImportCommand();

    public sealed class DevStubImportHandler : IJobHandler<DevStubImportCommand>
    {
        private readonly ImportService importService;
        private readonly IObjectSpaceFactory objectSpaceFactory;

        public DevStubImportHandler(ImportService importService, IObjectSpaceFactory objectSpaceFactory)
        {
            this.importService = importService;
            this.objectSpaceFactory = objectSpaceFactory;
        }

        public async Task ExecuteAsync(DevStubImportCommand command, CancellationToken ct = default)
        {
            Guid definitionId;
            using (var os = objectSpaceFactory.CreateObjectSpace<ImportDefinition>())
            {
                var definition = os.FirstOrDefault<ImportDefinition>(d => d.Name == "DevTest");
                if (definition == null)
                {
                    definition = os.CreateObject<ImportDefinition>();
                    definition.Name = "DevTest";
                    definition.SourceType = SourceType.File;
                    os.CommitChanges();
                }
                definitionId = definition.ID;
            }
            await importService.RunAsync(definitionId, new StubSource(), null, "dev stub (3 records)", ct);
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
