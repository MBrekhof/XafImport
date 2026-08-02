#nullable enable
using XafImport.Module.Jobs;

namespace XafImport.Module.Import
{
    public sealed record RunExportCommand(Guid DefinitionId);

    public sealed class RunExportHandler : IJobHandler<RunExportCommand>
    {
        private readonly ExportService exportService;

        public RunExportHandler(ExportService exportService) => this.exportService = exportService;

        public Task ExecuteAsync(RunExportCommand command, CancellationToken ct = default)
            => exportService.RunAsync(command.DefinitionId, ct);
    }
}
