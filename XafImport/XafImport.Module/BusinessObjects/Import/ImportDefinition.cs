#nullable enable
using System.Collections.ObjectModel;
using System.ComponentModel;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Model;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;

namespace XafImport.Module.BusinessObjects.Import
{
    public enum SourceType { File, SqlServer, Api }

    // Auto = detect by magic bytes; explicit value overrides detection.
    public enum FileFormat { Auto, Json, Xml, Xls, Pdf, Doc, Txt }

    public enum ErrorPolicy { ContinueSkipRecord, Abort }

    public enum TransferDirection { Import, Export }

    [DefaultClassOptions]
    [DefaultProperty(nameof(Name))]
    [NavigationItem("Import")]
    public class ImportDefinition : BaseObject
    {
        public virtual string Name { get; set; } = string.Empty;

        [ToolTip("Import: load external data into staging. Export: run SqlQuery and write the result to a file on the run.")]
        public virtual TransferDirection Direction { get; set; }

        public virtual SourceType SourceType { get; set; }
        public virtual FileFormat FileFormat { get; set; }
        public virtual bool Enabled { get; set; } = true;

        [ToolTip("Optional cron expression for scheduled background runs")]
        public virtual string? CronExpression { get; set; }

        [ToolTip("What to do when a record fails: skip it and continue, or abort the run")]
        public virtual ErrorPolicy OnError { get; set; }

        [ToolTip("With ContinueSkipRecord: abort anyway once this many records failed (0 = unlimited)")]
        public virtual int MaxErrors { get; set; }

        [ToolTip("Create an XAF notification when a run finishes (any outcome)")]
        public virtual bool NotifyOnCompletion { get; set; } = true;

        // SQL Server source. ponytail: stored plain until SRC-001 adds encryption (provider_settings pattern).
        public virtual string? SqlConnectionString { get; set; }
        public virtual string? SqlQuery { get; set; }

        // API source
        public virtual string? ApiEndpoint { get; set; }
        [ModelDefault("IsPassword", "true")]
        public virtual string? ApiKey { get; set; }

        [ToolTip("JSON column mapping used by the transform stage; empty = pass-through")]
        public virtual string? ColumnMappingJson { get; set; }

        // File source: the file to import. Uploaded via the standard XAF file attachment editor,
        // stored in DB so background (Hangfire) runs can read it without a temp-file handoff.
        [VisibleInDetailView(false)]
        [VisibleInListView(false)]
        public virtual Guid? UploadedFileId { get; set; }

        [System.ComponentModel.DataAnnotations.Schema.ForeignKey(nameof(UploadedFileId))]
        [Aggregated]
        [ExpandObjectMembers(ExpandObjectMembers.Never)]
        public virtual FileData? UploadedFile { get; set; }

        [Aggregated]
        public virtual IList<ImportRun> Runs { get; set; } = new ObservableCollection<ImportRun>();
    }
}
