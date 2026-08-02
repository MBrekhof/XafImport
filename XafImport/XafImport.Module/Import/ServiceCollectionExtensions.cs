#nullable enable
using Microsoft.Extensions.DependencyInjection;
using XafImport.Module.Import.Formats;

namespace XafImport.Module.Import
{
    public static class ServiceCollectionExtensions
    {
        // One-liner for host apps: everything the import/export pipeline needs except job
        // dispatch (Hangfire lives in the host - see docs/module-integration.md).
        public static IServiceCollection AddXafImportPipeline(this IServiceCollection services)
        {
            services.AddScoped<IStagingLoader, SqlServerStagingLoader>();
            services.AddScoped<ImportService>();
            services.AddScoped<ExportService>();
            // Registration order = magic-byte detection order; TXT is the catch-all and stays last.
            services.AddScoped<IFormatParser, JsonFormatParser>();
            services.AddScoped<IFormatParser, XmlFormatParser>();
            services.AddScoped<IFormatParser, XlsFormatParser>();
            services.AddScoped<IFormatParser, PdfFormatParser>();
            services.AddScoped<IFormatParser, DocFormatParser>();
            services.AddScoped<IFormatParser, TxtFormatParser>();
            services.AddScoped<FileSource>();
            services.AddScoped<SqlServerSource>();
            return services;
        }
    }
}
