#nullable enable
using DevExpress.ExpressApp;
using XafImport.Module.BusinessObjects.Import;
using XafImport.Module.Jobs;

namespace XafImport.Module.Import
{
    // Background import job (HF-002): serializable command carries only the definition id;
    // the handler reads everything else (incl. the uploaded file) from the database.
    public sealed record RunImportCommand(Guid DefinitionId);

    public sealed class RunImportHandler : IJobHandler<RunImportCommand>
    {
        private readonly ImportService importService;
        private readonly IObjectSpaceFactory objectSpaceFactory;
        private readonly FileSource fileSource;

        public RunImportHandler(ImportService importService, IObjectSpaceFactory objectSpaceFactory, FileSource fileSource)
        {
            this.importService = importService;
            this.objectSpaceFactory = objectSpaceFactory;
            this.fileSource = fileSource;
        }

        public async Task ExecuteAsync(RunImportCommand command, CancellationToken ct = default)
        {
            SourceType sourceType;
            byte[]? content;
            string? fileName;
            using (var os = objectSpaceFactory.CreateObjectSpace<ImportDefinition>())
            {
                var definition = os.GetObjectByKey<ImportDefinition>(command.DefinitionId)
                    ?? throw new ArgumentException($"ImportDefinition {command.DefinitionId} not found.");
                sourceType = definition.SourceType;
                content = definition.UploadedFile?.Content;
                fileName = definition.UploadedFile?.FileName;
            }

            switch (sourceType)
            {
                case SourceType.File:
                    if (content is not { Length: > 0 })
                    {
                        throw new InvalidOperationException("No file attached to the import definition. Upload a file first.");
                    }
                    using (var stream = new MemoryStream(content))
                    {
                        await importService.RunAsync(command.DefinitionId, fileSource, stream, fileName, ct);
                    }
                    break;
                default:
                    throw new NotSupportedException($"Source type {sourceType} is not implemented yet (SRC-001/SRC-002).");
            }
        }
    }
}
